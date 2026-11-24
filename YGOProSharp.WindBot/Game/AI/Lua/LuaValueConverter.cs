using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using YGOProSharp.Abstractions.Scripting;

namespace WindBot.Game.AI.Lua
{
    /// <summary>
    /// 处理跨 Lua 引擎（XLua、MoonSharp、NLua、Lua-CSharp 等）的常见返回值类型转换。
    /// 特别兼容 Lua number 统一映射为 <see cref="double"/> 或 <see cref="long"/>，以及 LuaTable 列表转换。
    /// </summary>
    internal static class LuaValueConverter
    {
        public static bool TryToBoolean(object value, out bool result)
        {
            switch (value)
            {
                case null:
                    result = false;
                    return false;
                case bool boolean:
                    result = boolean;
                    return true;
                case int intValue:
                    result = intValue != 0;
                    return true;
                case long longValue:
                    result = longValue != 0;
                    return true;
                case double doubleValue:
                    result = doubleValue != 0d;
                    return true;
                default:
                    result = false;
                    return false;
            }
        }

        public static bool TryToInt32(object value, out int result)
        {
            switch (value)
            {
                case null:
                    result = 0;
                    return false;
                case int intValue:
                    result = intValue;
                    return true;
                case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                    result = (int)longValue;
                    return true;
                case double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue)
                                             && doubleValue is >= int.MinValue and <= int.MaxValue:
                    result = (int)doubleValue;
                    return true;
                case float floatValue when !float.IsNaN(floatValue) && !float.IsInfinity(floatValue)
                                           && floatValue is >= int.MinValue and <= int.MaxValue:
                    result = (int)floatValue;
                    return true;
                case short shortValue:
                    result = shortValue;
                    return true;
                case byte byteValue:
                    result = byteValue;
                    return true;
                case uint uintValue when uintValue <= int.MaxValue:
                    result = (int)uintValue;
                    return true;
                case Enum enumValue:
                    result = Convert.ToInt32(enumValue, CultureInfo.InvariantCulture);
                    return true;
                case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed):
                    result = parsed;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }

        public static bool TryToUInt32(object value, out uint result)
        {
            switch (value)
            {
                case null:
                    result = 0u;
                    return false;
                case uint uintValue:
                    result = uintValue;
                    return true;
                case int intValue when intValue >= 0:
                    result = (uint)intValue;
                    return true;
                case long longValue when longValue is >= 0 and <= uint.MaxValue:
                    result = (uint)longValue;
                    return true;
                case double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue)
                                             && doubleValue is >= 0 and <= uint.MaxValue:
                    result = (uint)doubleValue;
                    return true;
                case Enum enumValue:
                    result = Convert.ToUInt32(enumValue, CultureInfo.InvariantCulture);
                    return true;
                case string text when uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed):
                    result = parsed;
                    return true;
                default:
                    result = 0u;
                    return false;
            }
        }

        public static bool TryToEnum<TEnum>(object value, out TEnum result) where TEnum : struct, Enum
        {
            if (value == null)
            {
                result = default;
                return false;
            }

            if (value is TEnum typed)
            {
                result = typed;
                return true;
            }

            if (value is string text && Enum.TryParse(text, ignoreCase: true, out TEnum parsed))
            {
                result = parsed;
                return true;
            }

            if (TryToInt32(value, out int numeric))
            {
                result = (TEnum)Enum.ToObject(typeof(TEnum), numeric);
                return true;
            }

            result = default;
            return false;
        }

        public static IList<ClientCard> TryToCardList(ILuaScriptContext context, object value)
        {
            if (value == null)
                return null;

            if (value is IList<ClientCard> directList)
                return directList;

            if (context != null && context.TryConvertList(value, out IList<ClientCard> converted) && converted != null)
                return converted;

            if (value is IEnumerable<ClientCard> typedEnumerable)
                return new List<ClientCard>(typedEnumerable);

            if (value is IEnumerable enumerable && value is not string)
            {
                List<ClientCard> cards = new List<ClientCard>();
                foreach (object item in enumerable)
                {
                    if (item is ClientCard card)
                        cards.Add(card);
                }
                return cards;
            }

            return null;
        }
    }
}
