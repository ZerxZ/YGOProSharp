namespace YGOProSharp.Tests;

public sealed class DiyAndReloadableResourceTests
{
    [Fact]
    public void CoreLayeredDiyResourcesOverrideCardsScriptsAndBanlists()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpDiyCore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. 多 CDB 覆盖 + 内存 DIY 卡片覆盖
            string baseDb = Path.Combine(tempDir, "cards.cdb");
            string diyDb = Path.Combine(tempDir, "diy.cdb");
            CreateTestCardDb(baseDb,
            [
                (1001, 1, 0, 0, (int)CardType.Monster, 4, 1, 1, 1800, 1000, "BaseOCG", "desc"),
                (1002, 2, 0, 0, (int)CardType.Monster, 4, 1, 1, 1700, 1000, "BaseTCG", "desc")
            ]);
            CreateTestCardDb(diyDb,
            [
                (1001, 3, 0, 0, (int)CardType.Monster, 4, 1, 1, 2500, 1200, "OverriddenBase", "override"),
                (9990001, 4, 0, 0, (int)CardType.Monster, 8, 1, 1, 3500, 2800, "DiyDragon", "custom")
            ]);

            SqliteCardDatabaseManager dbManager = new();
            ICardRepository layeredCards = dbManager.LoadCards([baseDb, diyDb]);
            AssertTrue(layeredCards.TryGetCard(1001, out Card overridden));
            AssertEqual(2500, overridden.Attack);
            AssertEqual(3, overridden.Ot);
            AssertTrue(layeredCards.TryGetCard(9990001, out Card diyCard));
            AssertEqual(4, diyCard.Ot);

            Card inMemoryCard = new(9990002, 4, 0, 0, (int)CardType.Spell, 0, 0, 0, 0, 0, 0, 0, 0);
            CompositeCardRepository compositeCards = new(new InMemoryCardRepository([inMemoryCard]), layeredCards);
            AssertTrue(compositeCards.TryGetCard(9990002, out Card memFound));
            AssertEqual((int)CardType.Spell, memFound.Type);
            AssertTrue(compositeCards.TryGetCard(1002, out _));

            // 2. DIY 卡片区域校验（Region=0/2 拒绝 ot=4，Region=3/5 放行 ot=4）
            Deck diyDeck = new(compositeCards);
            diyDeck.AddMain(9990001);
            DeckRules minOne = new(MainDeckMinSize: 1);
            AssertEqual(9990001, diyDeck.Check(null, ocg: true, tcg: true, allowCustom: false, minOne));
            AssertEqual(0, diyDeck.Check(null, ocg: true, tcg: true, allowCustom: true, minOne));
            AssertEqual((true, true, true), DefaultDeckValidator.ResolveRegionFlags(3));
            AssertEqual((false, false, false), DefaultDeckValidator.ResolveRegionFlags(4));

            // 3. 分层脚本（内存 > DIY 目录 > 基础目录 > .ypk 压缩包）+ 路径穿越保护
            string baseScriptDir = Path.Combine(tempDir, "script");
            string diyScriptDir = Path.Combine(tempDir, "diy", "script");
            Directory.CreateDirectory(baseScriptDir);
            Directory.CreateDirectory(diyScriptDir);
            File.WriteAllText(Path.Combine(baseScriptDir, "c1001.lua"), "-- base");
            File.WriteAllText(Path.Combine(diyScriptDir, "c1001.lua"), "-- diy dir");

            string ypkPath = Path.Combine(tempDir, "pack.ypk");
            using (ZipArchive archive = ZipFile.Open(ypkPath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("script/c9990001.lua");
                using StreamWriter sw = new(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                sw.Write("-- from ypk");
            }

            InMemoryScriptProvider memoryScripts = new();
            memoryScripts.RegisterText("c9990002.lua", "-- from memory");

            using CompositeScriptProvider compositeScripts = new(
                memoryScripts,
                new FileScriptProvider([diyScriptDir, baseScriptDir]),
                new ZipArchiveScriptProvider(ypkPath));

            AssertTrue(compositeScripts.TryGetScript("./script/c1001.lua", out byte[] s1));
            AssertEqual("-- diy dir", Encoding.UTF8.GetString(s1));
            AssertTrue(compositeScripts.TryGetScript("./script/c9990001.lua", out byte[] s2));
            AssertEqual("-- from ypk", Encoding.UTF8.GetString(s2));
            AssertTrue(compositeScripts.TryGetScript("c9990002.lua", out byte[] s3));
            AssertEqual("-- from memory", Encoding.UTF8.GetString(s3));
            AssertFalse(compositeScripts.TryGetScript("./script/../../secret.lua", out _));

            // 4. 多禁限表加载与名称/Hash 查询
            string baseLf = Path.Combine(tempDir, "lflist.conf");
            string diyLf = Path.Combine(tempDir, "diy_lflist.conf");
            File.WriteAllText(baseLf, "!2026.04 OCG\n1001 1\n");
            File.WriteAllText(diyLf, "!DIY Custom\n$whitelist\n9990001 3\n");

            BanlistManager.Init([diyLf, baseLf]);
            AssertEqual(2, BanlistManager.Banlists.Count);
            AssertEqual("DIY Custom", BanlistManager.Banlists[0].Name);
            AssertTrue(BanlistManager.Banlists[0].WhitelistOnly);
            AssertEqual(3, BanlistManager.Banlists[0].GetQuantity(9990001));
            AssertEqual(0, BanlistManager.Banlists[0].GetQuantity(1001));
            AssertTrue(BanlistManager.FindByName("2026.04 OCG") is not null);
            AssertTrue(BanlistManager.TryGetByHash(BanlistManager.Banlists[1].Hash, out Banlist? byHash));
            AssertEqual("2026.04 OCG", byHash!.Name);
        }
        finally
        {
            BanlistManager.Banlists.Clear();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CoreReloadableProvidersAndDiyPackageManagerValidateAndHotSwap()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpDiyPkg_" + Guid.NewGuid().ToString("N"));
        string baseScriptDir = Path.Combine(tempDir, "script");
        string diyRoot = Path.Combine(tempDir, "diy");
        Directory.CreateDirectory(baseScriptDir);
        Directory.CreateDirectory(diyRoot);

        try
        {
            SqliteCardDatabaseManager dbManager = new();
            string baseDbPath = Path.Combine(tempDir, "cards.cdb");
            CreateTestCardDb(baseDbPath,
            [
                (1001, 1, 0, 0, (int)CardType.Monster | (int)CardType.Normal, 4, 1, 1, 1800, 1000, "BaseWarrior", "desc")
            ]);
            File.WriteAllText(Path.Combine(tempDir, "lflist.conf"), "!BaseLF\n1001 3\n");

            DiyPackageManager pkgManager = new(diyRoot, dbManager);

            // 1. 包 ID 与路径穿越防护 + Lua 静态安全审计 + ZIP Slip 防御
            AssertFalse(DiyPackageManager.IsValidPackageId("../escape"));
            AssertFalse(DiyPackageManager.IsValidPackageId("bad/slash"));
            AssertTrue(DiyPackageManager.IsValidPackageId("pack-alpha_01"));

            LuaScriptSecurityAudit unsafeLua = DiyPackageManager.AuditLuaScript("function c1.initial_effect(c)\n os.execute('calc')\nend");
            AssertFalse(unsafeLua.IsSafe);
            LuaScriptSecurityAudit safeLua = DiyPackageManager.AuditLuaScript("-- os.execute in comment is ignored\nfunction c9990001.initial_effect(c)\n local video = 1\nend");
            AssertTrue(safeLua.IsSafe);

            using (MemoryStream zipBuf = new())
            {
                using (ZipArchive zip = new(zipBuf, ZipArchiveMode.Create, leaveOpen: true))
                {
                    zip.CreateEntry("../evil.lua");
                }
                zipBuf.Position = 0;
                IReadOnlyList<string> archiveErrors = DiyPackageManager.AuditArchiveEntries(zipBuf);
                AssertTrue(archiveErrors.Count > 0);
            }

            // 2. 创建受管 DIY 包、写入临时 .cdb、检测覆盖冲突与缺失脚本、补齐脚本后通过校验
            pkgManager.UpsertManifest(new DiyPackageManifest
            {
                Id = "pack-alpha",
                Name = "Alpha DIY Pack",
                Author = "Tester",
                Priority = 200,
                Enabled = true
            });

            string stagingCdb = Path.Combine(tempDir, "staging_alpha.cdb");
            CreateTestCardDb(stagingCdb,
            [
                (1001, 4, 0, 0, (int)CardType.Monster | (int)CardType.Normal, 4, 1, 1, 2500, 1200, "AlphaWarrior", "override"),
                (9990001, 4, 0, 0, (int)CardType.Monster | (int)CardType.Effect, 8, 1, 1, 3200, 2800, "AlphaDragon", "effect")
            ]);
            CardDatabaseValidationResult saveDbRes = pkgManager.SaveCardDatabase("pack-alpha", File.ReadAllBytes(stagingCdb));
            AssertTrue(saveDbRes.IsValid);
            AssertEqual(2, saveDbRes.TotalCards);
            AssertEqual(2, saveDbRes.CustomCards);

            ICardRepository baseRepo = dbManager.LoadCards(baseDbPath);
            DiyPackageValidationReport reportBeforeScript = pkgManager.ValidatePackage("pack-alpha", baseRepo);
            AssertTrue(reportBeforeScript.IsValid);
            AssertTrue(reportBeforeScript.OverriddenCardIds.Contains(1001));
            AssertTrue(reportBeforeScript.MissingScriptCardIds.Contains(9990001));

            AssertFalse(pkgManager.SaveScript("pack-alpha", "c9990001.lua", "io.open('secret')").IsSafe);
            AssertTrue(pkgManager.SaveScript("pack-alpha", "c9990001.lua", "-- alpha dragon\nfunction c9990001.initial_effect(c) end").IsSafe);
            pkgManager.SaveDeck("pack-alpha", "AlphaDeck", "#main\n9990001\n1001\n#extra\n!side\n");

            DiyPackageValidationReport reportAfterScript = pkgManager.ValidatePackage("pack-alpha", baseRepo);
            AssertEqual(0, reportAfterScript.MissingScriptCardIds.Count);

            // 3. 软卸载至 .trash 与一键恢复
            string trashEntry = pkgManager.MoveToTrash("pack-alpha");
            AssertEqual(0, pkgManager.ListPackages().Count);
            string restoredId = pkgManager.RestoreFromTrash(trashEntry);
            AssertEqual("pack-alpha", restoredId);
            AssertEqual(1, pkgManager.ListPackages(enabledOnly: true).Count);

            // 4. 服务端启动后动态新增第二个高优先级 DIY 包并执行免重启热重载
            ServerOptions serverOptions = new()
            {
                RootPath = tempDir,
                EnableNetworkListener = false,
                AutoLoadExpansions = true
            };
            using ServerTestFakeOcgRuntime fakeRuntime = new();
            using YGOProSharpServerHost host = YGOProSharpServer.CreateHost(serverOptions, fakeRuntime);

            AssertEqual(1L, host.Server.ReloadableCards.CatalogVersion);
            AssertTrue(host.Server.CardRepository.TryGetCard(9990001, out Card loadedAlpha));
            AssertEqual(3200, loadedAlpha.Attack);
            AssertEqual(1, host.Server.PresetDecks.Count);

            pkgManager.UpsertManifest(new DiyPackageManifest { Id = "pack-beta", Name = "Beta Pack", Priority = 300, Enabled = true });
            string stagingBeta = Path.Combine(tempDir, "staging_beta.cdb");
            CreateTestCardDb(stagingBeta,
            [
                (9990001, 4, 0, 0, (int)CardType.Monster | (int)CardType.Normal, 8, 1, 1, 4000, 3500, "BetaDragon", "buffed")
            ]);
            pkgManager.SaveCardDatabase("pack-beta", File.ReadAllBytes(stagingBeta));
            pkgManager.SaveScript("pack-beta", "c9990001.lua", "-- beta override");
            pkgManager.SaveDeck("pack-beta", "BetaDeck", "#main\n9990001\n#extra\n!side\n");

            ServerReloadResult reloadRes = host.Server.ReloadAssets();
            AssertEqual(2L, reloadRes.CatalogVersion);
            AssertEqual(2L, reloadRes.ScriptVersion);
            AssertEqual(2, reloadRes.PresetDeckCount);
            AssertTrue(host.Server.CardRepository.TryGetCard(9990001, out Card reloadedCard));
            AssertEqual(4000, reloadedCard.Attack);
            AssertTrue(fakeRuntime.LastOptions!.ScriptProvider.TryGetScript("./script/c9990001.lua", out byte[] scriptBytes));
            AssertEqual("-- beta override", Encoding.UTF8.GetString(scriptBytes));
        }
        finally
        {
            BanlistManager.Banlists.Clear();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CoreDiyCardEditorConflictAnalyzerStringsConfAndYpkExportWork()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpDiyPhase2Core_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            SqliteCardDatabaseManager dbMgr = new();
            DiyPackageManager pkgMgr = new(tempDir, dbMgr);
            pkgMgr.UpsertManifest(new DiyPackageManifest { Id = "pack-one", Name = "Pack One", Enabled = true });

            // 1. Working Copy 单卡写入（覆盖效果怪兽、灵摆左右刻度、Link 箭头与提示字符串）
            CardDatabaseValidationResult r1 = pkgMgr.UpsertCard(
                "pack-one",
                new DiyCardEntry
                {
                    Id = 99000101,
                    Ot = 0x4,
                    Setcode = 0xa100,
                    Type = (int)(CardType.Monster | CardType.Effect | CardType.Pendulum),
                    Level = 7,
                    LScale = 2,
                    RScale = 8,
                    Race = (int)CardRace.Warrior,
                    Attribute = (int)CardAttribute.Dark,
                    Attack = 2500,
                    Defense = 2000,
                    Name = "星辉灵摆魔导",
                    Desc = "灵摆与怪兽效果",
                    Strings = ["灵摆回血", "检索星辉"]
                },
                "local s,id=GetID()\nfunction s.initial_effect(c)\nend\n");
            AssertTrue(r1.IsValid);

            CardDatabaseValidationResult r2 = pkgMgr.UpsertCard(
                "pack-one",
                new DiyCardEntry
                {
                    Id = 99000102,
                    Ot = 0x4,
                    Type = (int)(CardType.Monster | CardType.Effect | CardType.Link),
                    Level = 2,
                    LinkMarker = 0x05,
                    Race = (int)CardRace.Cyberse,
                    Attribute = (int)CardAttribute.Light,
                    Attack = 1800,
                    Name = "星辉连接者",
                    Desc = "效果怪兽2只"
                },
                "local s,id=GetID()\nfunction s.initial_effect(c)\nend\n");
            AssertTrue(r2.IsValid);

            DiyCardDetail? pendDetail = pkgMgr.GetCardDetail("pack-one", 99000101);
            AssertTrue(pendDetail is not null && pendDetail.Card.LScale == 2 && pendDetail.Card.RScale == 8);
            AssertEqual("灵摆回血", pendDetail!.Card.Strings[0]);

            DiyCardDetail? linkDetail = pkgMgr.GetCardDetail("pack-one", 99000102);
            AssertTrue(linkDetail is not null && linkDetail.Card.LinkMarker == 0x05 && linkDetail.Card.Defense == 0);

            // 2. strings.conf 与 PNG 卡图魔数头校验
            StringsConfSummary conf = pkgMgr.SaveStringsConf("pack-one", "!setname 0xa100 星辉\n!counter 0x1999 星辉指示物\n");
            AssertEqual(1, conf.Setnames.Count);
            AssertEqual(1, conf.Counters.Count);
            AssertEqual("星辉", conf.Setnames[0].Name);

            AssertThrows<InvalidOperationException>(() => pkgMgr.SaveCardImage("pack-one", 99000101, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]));
            byte[] minimalPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
            string savedImg = pkgMgr.SaveCardImage("pack-one", 99000101, minimalPng);
            AssertEqual("99000101.png", savedImg);
            var readImg = pkgMgr.TryReadCardImage("pack-one", 99000101);
            AssertTrue(readImg is not null && readImg.Value.ContentType == "image/png");

            // 3. 跨包重复卡密冲突巡检与 .ypk 客户端扩展包导出
            pkgMgr.UpsertManifest(new DiyPackageManifest { Id = "pack-two", Name = "Pack Two", Enabled = true });
            pkgMgr.UpsertCard("pack-two", new DiyCardEntry { Id = 99000101, Type = (int)(CardType.Monster | CardType.Normal), Name = "同名冲突卡" });

            DiyGlobalConflictReport conflicts = pkgMgr.AnalyzeAllPackageConflicts();
            AssertTrue(conflicts.Conflicts.Any(c => c.CardId == 99000101 && c.DefinedInPackages.Count == 2));
            AssertTrue(conflicts.MissingImagesByPackage.ContainsKey("pack-one")); // 99000102 尚未上传卡图

            byte[] ypkBytes = pkgMgr.ExportPackageAsYpk("pack-one");
            using (MemoryStream ypkStream = new(ypkBytes))
            {
                AssertEqual(0, DiyPackageManager.AuditArchiveEntries(ypkStream).Count);
            }

            string ypkFile = Path.Combine(tempDir, "pack-one.ypk");
            File.WriteAllBytes(ypkFile, ypkBytes);
            using (ZipArchiveScriptProvider zipScripts = new(ypkFile, copyToMemory: true))
            {
                AssertTrue(zipScripts.TryGetScript("./script/c99000101.lua", out _));
            }

            AssertTrue(pkgMgr.DeleteCard("pack-one", 99000102));
            AssertTrue(pkgMgr.GetCardDetail("pack-one", 99000102) is null);

            // 4. .ypk 整包原子解包导入回环、单层包裹目录自动剥离、恶意压缩包拦截与全服合集 .ypk 导出
            pkgMgr.SaveDeck("pack-one", "StarlightSample", "#main\n99000101\n#extra\n!side\n");
            byte[] fullYpk = pkgMgr.ExportPackageAsYpk("pack-one");
            string sanitizedCnId = DiyPackageManager.SanitizePackageIdFromFileName("星辉扩展包 v2.0.ypk");
            AssertTrue(DiyPackageManager.IsValidPackageId(sanitizedCnId));

            using (MemoryStream importStream = new(fullYpk))
            {
                DiyYpkImportResult imp = pkgMgr.ImportPackageFromYpk(
                    "pack-roundtrip",
                    importStream,
                    displayName: "Roundtrip Pack",
                    author: "Importer",
                    mergeIntoExisting: false);
                AssertTrue(imp.IsSuccess);
                AssertEqual(1, imp.TotalCards);
                AssertTrue(imp.ImportedScripts >= 1);
                AssertEqual(1, imp.ImportedImages);
                AssertEqual(1, imp.ImportedDecks);
                AssertEqual(1, imp.ImportedConfFiles);
            }

            DiyCardDetail? importedCard = pkgMgr.GetCardDetail("pack-roundtrip", 99000101);
            AssertTrue(importedCard is not null && importedCard.HasImage && !string.IsNullOrEmpty(importedCard.ScriptContent));
            AssertEqual("星辉灵摆魔导", importedCard!.Card.Name);
            AssertEqual(1, pkgMgr.ReadStringsConf("pack-roundtrip").Setnames.Count);

            // 校验带恶意 Lua 的 .ypk 导入被原子拦截且不残留包目录
            using (MemoryStream badYpk = new())
            {
                using (ZipArchive badZip = new(badYpk, ZipArchiveMode.Create, leaveOpen: true))
                {
                    ZipArchiveEntry badEntry = badZip.CreateEntry("script/c99000999.lua");
                    using StreamWriter sw = new(badEntry.Open(), new UTF8Encoding(false));
                    sw.Write("os.execute('whoami')");
                }
                badYpk.Position = 0;
                DiyYpkImportResult badImp = pkgMgr.ImportPackageFromYpk("pack-malicious", badYpk);
                AssertFalse(badImp.IsSuccess);
                AssertFalse(Directory.Exists(pkgMgr.GetPackageDirectory("pack-malicious")));
            }

            // 全服多启用包按优先级合并导出为一个 .ypk
            byte[] bundleYpk = pkgMgr.ExportAllEnabledPackagesAsYpk();
            using (MemoryStream bundleStream = new(bundleYpk))
            {
                DiyYpkImportResult bundleImp = pkgMgr.ImportPackageFromYpk("pack-bundle-verify", bundleStream, mergeIntoExisting: false);
                AssertTrue(bundleImp.IsSuccess);
                AssertTrue(bundleImp.TotalCards >= 1);
            }

            // 5. .ypk 在线文件目录树透视、单文件/卡图按需安全提取、预览暂存与大体积提案落盘验证
            using (MemoryStream previewStream = new(fullYpk))
            {
                (string previewToken, DiyYpkInspectionReport report) = pkgMgr.SavePreviewYpk(previewStream, "星辉测试包.ypk");
                AssertTrue(!string.IsNullOrWhiteSpace(previewToken));
                AssertTrue(report.IsSafe);
                AssertTrue(report.TotalFiles >= 4);
                AssertEqual(1, report.CardCount);
                AssertTrue(report.ScriptCount >= 1);
                AssertEqual(1, report.ImageCount);
                AssertEqual(1, report.DeckCount);
                AssertEqual(1, report.StringsConf.Setnames.Count);

                AssertTrue(pkgMgr.TryGetPreviewYpkPath(previewToken, out string savedPreviewPath, out string savedOrigName));
                AssertEqual("星辉测试包.ypk", savedOrigName);

                using (FileStream entryFs = File.OpenRead(savedPreviewPath))
                {
                    var luaEntry = DiyPackageManager.TryReadYpkArchiveEntry(entryFs, entryPath: "script/c99000101.lua");
                    AssertTrue(luaEntry is not null && luaEntry.Value.IsText && luaEntry.Value.TextContent.Contains("initial_effect", StringComparison.Ordinal));

                    var imgEntry = DiyPackageManager.TryReadYpkArchiveEntry(entryFs, cardImageId: 99000101);
                    AssertTrue(imgEntry is not null && !imgEntry.Value.IsText && imgEntry.Value.ContentType == "image/png" && imgEntry.Value.Bytes.Length > 0);

                    // 路径穿越尝试必须返回 null
                    AssertTrue(DiyPackageManager.TryReadYpkArchiveEntry(entryFs, entryPath: "../secrets.txt") is null);
                }

                string stagedProposalFile = pkgMgr.SaveStagedProposalYpk(savedPreviewPath);
                AssertTrue(pkgMgr.TryGetStagedProposalYpkPath(stagedProposalFile, out string stagedFullPath) && File.Exists(stagedFullPath));
                AssertFalse(pkgMgr.TryGetStagedProposalYpkPath("../evil.ypk", out _));
            }

            DiyYpkInspectionReport installedReport = pkgMgr.InspectInstalledPackage("pack-roundtrip");
            AssertTrue(installedReport.IsSafe);
            AssertEqual(1, installedReport.CardCount);
            AssertTrue(installedReport.TotalFiles >= 4);
            var installedConf = pkgMgr.TryReadInstalledPackageFile("pack-roundtrip", "strings.conf");
            AssertTrue(installedConf is not null && installedConf.Value.IsText && installedConf.Value.TextContent.Contains("!setname", StringComparison.Ordinal));

            // 6. 回归验证：包含 64 位 category 位掩码 (> Int32.MaxValue) 与 uint32 高位符号位 (0x80000000) 的 DataEditorX 风格 .ypk 不抛 OverflowException
            string highBitCdbPath = Path.Combine(tempDir, "highbit.cdb");
            using (Microsoft.Data.Sqlite.SqliteConnection hbConn = new($"Data Source={highBitCdbPath};Pooling=False"))
            {
                hbConn.Open();
                using var createCmd = hbConn.CreateCommand();
                createCmd.CommandText =
                    "CREATE TABLE datas (id INTEGER PRIMARY KEY, ot INTEGER, alias INTEGER, setcode INTEGER, type INTEGER, atk INTEGER, def INTEGER, level INTEGER, race INTEGER, attribute INTEGER, category INTEGER);" +
                    "CREATE TABLE texts (id INTEGER PRIMARY KEY, name TEXT, desc TEXT, str1 TEXT);";
                createCmd.ExecuteNonQuery();

                using var insData = hbConn.CreateCommand();
                insData.CommandText =
                    "INSERT INTO datas VALUES (99887766, 4, 0, 4294967296, 2147483681, 2500, 2000, 2164785156, 2147483648, 32, 4611686020574871552);" +
                    "INSERT INTO texts VALUES (99887766, '高维幻想龙', '64位category与uint32高位测试', '提示1');";
                insData.ExecuteNonQuery();
            }

            using (MemoryStream highBitYpk = new())
            {
                using (ZipArchive hz = new(highBitYpk, ZipArchiveMode.Create, leaveOpen: true))
                {
                    ZipArchiveEntry cdbEntry = hz.CreateEntry("cards.cdb");
                    using Stream es = cdbEntry.Open();
                    byte[] cdbBytes = File.ReadAllBytes(highBitCdbPath);
                    es.Write(cdbBytes, 0, cdbBytes.Length);
                }

                highBitYpk.Position = 0;
                (_, DiyYpkInspectionReport hbReport) = pkgMgr.SavePreviewYpk(highBitYpk, "highbit_64.ypk");
                AssertTrue(hbReport.IsSafe);
                AssertEqual(1, hbReport.CardCount);
                AssertEqual(99887766, hbReport.Cards[0].Card.Id);
                AssertEqual(4611686020574871552L, hbReport.Cards[0].Card.Category);
                AssertEqual(unchecked((int)2147483648L), hbReport.Cards[0].Card.Race);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

}
