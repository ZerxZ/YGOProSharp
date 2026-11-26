using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace YGOProSharp.Generators;

/// <summary>
/// Roslyn 增量源生成器：扫描标注了 <c>[HttpGet]</c> / <c>[HttpPost]</c> / <c>[HttpPut]</c> / <c>[HttpPatch]</c> / <c>[HttpDelete]</c> / <c>[MapEndpoint]</c>
/// 的局部类方法，在编译期生成零反射 Minimal API 路由注册、认证/RBAC/CSRF 门禁、限流策略挂载与 <c>AuthenticatedPrincipal</c> 参数自动注入代码。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class WebEndpointMappingGenerator : IIncrementalGenerator
{
    private const string WebRouteBaseFullName = "YGOProSharp.Web.Routing.WebRouteAttribute";
    private const string AdminAuthAttrFullName = "YGOProSharp.Web.Routing.AdminAuthAttribute";
    private const string PortalAuthAttrFullName = "YGOProSharp.Web.Routing.PortalAuthAttribute";
    private const string RateLimitAttrFullName = "YGOProSharp.Web.Routing.RateLimitAttribute";
    private const string PrincipalFullName = "YGOProSharp.Persistence.AuthenticatedPrincipal";
    private const string HttpContextFullName = "Microsoft.AspNetCore.Http.HttpContext";

    private static readonly SymbolDisplayFormat FullyQualifiedNullableFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly DiagnosticDescriptor MissingAuthAttributeRule = new(
        id: "YGOWEB001",
        title: "Endpoint with AuthenticatedPrincipal requires [AdminAuth] or [PortalAuth]",
        messageFormat: "Endpoint method '{0}' declares an AuthenticatedPrincipal parameter but has neither [AdminAuth] nor [PortalAuth]",
        category: "YGOProSharp.Web.Routing",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<EndpointExtractionResult?> endpoints = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
            transform: static (ctx, _) => ExtractEndpoint(ctx))
            .Where(static x => x is not null);

        context.RegisterSourceOutput(endpoints.Collect(), static (spc, items) =>
        {
            if (items.IsDefaultOrEmpty)
                return;

            List<EndpointModel> validModels = new(items.Length);
            foreach (EndpointExtractionResult? item in items)
            {
                if (item is null)
                    continue;

                if (item.MissingAuthDiagnosticLocation is not null)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        MissingAuthAttributeRule,
                        item.MissingAuthDiagnosticLocation,
                        item.Model.MethodName));
                    continue;
                }

                validModels.Add(item.Model);
            }

            if (validModels.Count == 0)
                return;

            foreach (IGrouping<string, EndpointModel> hostGroup in validModels.GroupBy(static m => m.ContainingTypeFullName, StringComparer.Ordinal))
            {
                EndpointModel first = hostGroup.First();
                string source = GenerateHostMappingsSource(first, hostGroup.ToList());
                spc.AddSource($"{first.ContainingTypeName}.Endpoints.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        });
    }

    private static EndpointExtractionResult? ExtractEndpoint(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not MethodDeclarationSyntax methodSyntax)
            return null;

        if (ctx.SemanticModel.GetDeclaredSymbol(methodSyntax) is not IMethodSymbol methodSymbol)
            return null;

        AttributeData? routeAttr = null;
        AttributeData? adminAuthAttr = null;
        AttributeData? portalAuthAttr = null;
        List<string> rateLimits = new();

        foreach (AttributeData attr in methodSymbol.GetAttributes())
        {
            INamedTypeSymbol? attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            string fullName = attrClass.ToDisplayString();
            if (InheritsFrom(attrClass, WebRouteBaseFullName))
            {
                routeAttr = attr;
            }
            else if (fullName == AdminAuthAttrFullName)
            {
                adminAuthAttr = attr;
            }
            else if (fullName == PortalAuthAttrFullName)
            {
                portalAuthAttr = attr;
            }
            else if (fullName == RateLimitAttrFullName &&
                     attr.ConstructorArguments.Length > 0 &&
                     attr.ConstructorArguments[0].Value is string rlPolicy &&
                     !string.IsNullOrWhiteSpace(rlPolicy))
            {
                rateLimits.Add(rlPolicy);
            }
        }

        if (routeAttr is null || methodSymbol.ContainingType is null)
            return null;

        string httpVerb = ResolveHttpVerb(routeAttr);
        string pattern = routeAttr.ConstructorArguments.Length > 0
            ? (routeAttr.ConstructorArguments[0].Value as string ?? "/")
            : "/";
        string group = GetNamedStringArgument(routeAttr, "Group") ?? string.Empty;

        bool hasAdminAuth = adminAuthAttr is not null;
        bool adminAllowMustChange = hasAdminAuth && GetNamedBoolArgument(adminAuthAttr!, "AllowWhenMustChangePassword");
        List<string> adminPerms = new();
        if (hasAdminAuth && adminAuthAttr!.ConstructorArguments.Length > 0)
        {
            TypedConstant arg0 = adminAuthAttr.ConstructorArguments[0];
            if (arg0.Kind == TypedConstantKind.Array && !arg0.Values.IsDefaultOrEmpty)
            {
                foreach (TypedConstant item in arg0.Values)
                {
                    if (item.Value is string s && !string.IsNullOrWhiteSpace(s))
                        adminPerms.Add(s);
                }
            }
        }

        bool hasPortalAuth = portalAuthAttr is not null;
        bool portalAllowMustChange = hasPortalAuth && GetNamedBoolArgument(portalAuthAttr!, "AllowWhenMustChangePassword");

        List<EndpointParamModel> parameters = new(methodSymbol.Parameters.Length);
        bool usesPrincipal = false;
        string? existingHttpContextParamName = null;

        foreach (IParameterSymbol p in methodSymbol.Parameters)
        {
            string plainType = p.Type.ToDisplayString();
            bool isPrincipal = plainType == PrincipalFullName || plainType == PrincipalFullName + "?";
            bool isHttpCtx = plainType == HttpContextFullName;

            if (isPrincipal)
                usesPrincipal = true;
            if (isHttpCtx && existingHttpContextParamName is null)
                existingHttpContextParamName = EscapeIdentifier(p.Name);

            string fqType = p.Type.ToDisplayString(FullyQualifiedNullableFormat);
            parameters.Add(new EndpointParamModel(EscapeIdentifier(p.Name), fqType, isPrincipal));
        }

        Location? diagLocation = null;
        if (usesPrincipal && !hasAdminAuth && !hasPortalAuth)
            diagLocation = methodSyntax.Identifier.GetLocation();

        INamedTypeSymbol containingType = methodSymbol.ContainingType;
        string ns = containingType.ContainingNamespace is { IsGlobalNamespace: false } cns
            ? cns.ToDisplayString()
            : string.Empty;

        EndpointModel model = new(
            ContainingNamespace: ns,
            ContainingTypeName: containingType.Name,
            ContainingTypeFullName: containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            TypeAccessibilityKeyword: AccessibilityToKeyword(containingType.DeclaredAccessibility),
            MethodName: EscapeIdentifier(methodSymbol.Name),
            IsStaticMethod: methodSymbol.IsStatic,
            HttpVerb: httpVerb,
            Pattern: pattern,
            Group: group,
            HasAdminAuth: hasAdminAuth,
            AdminAllowMustChange: adminAllowMustChange,
            AdminPermissions: adminPerms.ToArray(),
            HasPortalAuth: hasPortalAuth,
            PortalAllowMustChange: portalAllowMustChange,
            RateLimitPolicies: rateLimits.ToArray(),
            UsesPrincipal: usesPrincipal,
            ExistingHttpContextParamName: existingHttpContextParamName,
            Parameters: parameters.ToArray(),
            SourceFilePath: methodSyntax.SyntaxTree.FilePath ?? string.Empty,
            SourceSpanStart: methodSyntax.SpanStart);

        return new EndpointExtractionResult(model, diagLocation);
    }

    private static string GenerateHostMappingsSource(EndpointModel hostSample, List<EndpointModel> endpoints)
    {
        StringBuilder sb = new(endpoints.Count * 360 + 512);
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        bool hasNs = !string.IsNullOrEmpty(hostSample.ContainingNamespace);
        if (hasNs)
        {
            sb.Append("namespace ").Append(hostSample.ContainingNamespace).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append(hostSample.TypeAccessibilityKeyword).Append(" partial class ").AppendLine(hostSample.ContainingTypeName);
        sb.AppendLine("{");

        var byGroup = endpoints
            .OrderBy(static e => e.SourceFilePath, StringComparer.Ordinal)
            .ThenBy(static e => e.SourceSpanStart)
            .GroupBy(static e => e.Group, StringComparer.Ordinal);

        foreach (IGrouping<string, EndpointModel> grp in byGroup.OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            string mapMethodName = string.IsNullOrWhiteSpace(grp.Key)
                ? "MapGeneratedEndpoints"
                : $"MapGenerated{SanitizeIdentifier(grp.Key)}Endpoints";

            sb.Append("    internal void ").Append(mapMethodName).AppendLine("(global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)");
            sb.AppendLine("    {");

            foreach (EndpointModel ep in grp)
            {
                EmitSingleEndpoint(sb, ep);
            }

            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitSingleEndpoint(StringBuilder sb, EndpointModel ep)
    {
        string mapFn = ep.HttpVerb switch
        {
            "GET" => "MapGet",
            "POST" => "MapPost",
            "PUT" => "MapPut",
            "PATCH" => "MapPatch",
            "DELETE" => "MapDelete",
            _ => "Map"
        };

        string httpParamForPrincipal = ep.ExistingHttpContextParamName ?? "__http";
        List<string> lambdaParams = new(ep.Parameters.Length + 1);
        if (ep.UsesPrincipal && ep.ExistingHttpContextParamName is null)
            lambdaParams.Add("global::Microsoft.AspNetCore.Http.HttpContext __http");

        List<string> callArgs = new(ep.Parameters.Length);
        foreach (EndpointParamModel p in ep.Parameters)
        {
            if (p.IsPrincipal)
            {
                callArgs.Add($"global::YGOProSharp.Web.Security.WebSecurityExtensions.GetRequiredPrincipal({httpParamForPrincipal})");
            }
            else
            {
                lambdaParams.Add($"{p.FullyQualifiedType} {p.Name}");
                callArgs.Add(p.Name);
            }
        }

        string targetPrefix = ep.IsStaticMethod ? ep.ContainingTypeName : "this";
        string lambdaExpr = $"({string.Join(", ", lambdaParams)}) => {targetPrefix}.{ep.MethodName}({string.Join(", ", callArgs)})";
        string patternLiteral = SymbolDisplay.FormatLiteral(ep.Pattern, quote: true);

        string expr = $"global::Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.{mapFn}(app, {patternLiteral}, {lambdaExpr})";

        if (ep.HasAdminAuth)
        {
            string allowStr = ep.AdminAllowMustChange ? "true" : "false";
            if (ep.AdminPermissions.Length == 0)
            {
                expr = $"global::YGOProSharp.Web.Security.WebSecurityExtensions.RequireAdminAuth({expr}, {allowStr})";
            }
            else
            {
                string permsLiteral = string.Join(", ", ep.AdminPermissions.Select(static s => SymbolDisplay.FormatLiteral(s, quote: true)));
                expr = $"global::YGOProSharp.Web.Security.WebSecurityExtensions.RequireAdminAuth({expr}, {allowStr}, {permsLiteral})";
            }
        }

        if (ep.HasPortalAuth)
        {
            string allowStr = ep.PortalAllowMustChange ? "true" : "false";
            expr = $"global::YGOProSharp.Web.Security.WebSecurityExtensions.RequirePortalAuth({expr}, {allowStr})";
        }

        foreach (string rlPolicy in ep.RateLimitPolicies)
        {
            string policyLit = SymbolDisplay.FormatLiteral(rlPolicy, quote: true);
            expr = $"global::Microsoft.AspNetCore.Builder.RateLimiterEndpointConventionBuilderExtensions.RequireRateLimiting({expr}, {policyLit})";
        }

        sb.Append("        ").Append(expr).AppendLine(";");
    }

    private static bool InheritsFrom(INamedTypeSymbol? symbol, string baseFullName)
    {
        for (INamedTypeSymbol? cur = symbol; cur is not null; cur = cur.BaseType)
        {
            if (cur.ToDisplayString() == baseFullName)
                return true;
        }
        return false;
    }

    private static string ResolveHttpVerb(AttributeData routeAttr)
    {
        string attrName = routeAttr.AttributeClass?.Name ?? string.Empty;
        return attrName switch
        {
            "HttpGetAttribute" => "GET",
            "HttpPostAttribute" => "POST",
            "HttpPutAttribute" => "PUT",
            "HttpPatchAttribute" => "PATCH",
            "HttpDeleteAttribute" => "DELETE",
            "MapEndpointAttribute" => "ANY",
            _ => routeAttr.ConstructorArguments.Length > 1 && routeAttr.ConstructorArguments[0].Value is string m ? m : "GET"
        };
    }

    private static string? GetNamedStringArgument(AttributeData attr, string name)
    {
        foreach (KeyValuePair<string, TypedConstant> kv in attr.NamedArguments)
        {
            if (kv.Key == name && kv.Value.Value is string s)
                return s;
        }
        return null;
    }

    private static bool GetNamedBoolArgument(AttributeData attr, string name)
    {
        foreach (KeyValuePair<string, TypedConstant> kv in attr.NamedArguments)
        {
            if (kv.Key == name && kv.Value.Value is bool b)
                return b;
        }
        return false;
    }

    private static string AccessibilityToKeyword(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        _ => "internal"
    };

    private static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private static string SanitizeIdentifier(string raw)
    {
        StringBuilder sb = new(raw.Length);
        foreach (char c in raw)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
        }
        return sb.Length == 0 ? "Custom" : sb.ToString();
    }

    private sealed record EndpointParamModel(
        string Name,
        string FullyQualifiedType,
        bool IsPrincipal);

    private sealed record EndpointModel(
        string ContainingNamespace,
        string ContainingTypeName,
        string ContainingTypeFullName,
        string TypeAccessibilityKeyword,
        string MethodName,
        bool IsStaticMethod,
        string HttpVerb,
        string Pattern,
        string Group,
        bool HasAdminAuth,
        bool AdminAllowMustChange,
        string[] AdminPermissions,
        bool HasPortalAuth,
        bool PortalAllowMustChange,
        string[] RateLimitPolicies,
        bool UsesPrincipal,
        string? ExistingHttpContextParamName,
        EndpointParamModel[] Parameters,
        string SourceFilePath,
        int SourceSpanStart);

    private sealed record EndpointExtractionResult(
        EndpointModel Model,
        Location? MissingAuthDiagnosticLocation);
}
