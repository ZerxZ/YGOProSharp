-- Group 库全部 40 个 native Lua API 精确签名（对照 ygopro-core/libgroup.cpp）

function register(add, sig)
    add("Group.CreateGroup", sig(
        "dot",
        "创建一个空的卡片集合（默认在顶层脚本调用结束后自动回收，跨回调保存需调用 `KeepAlive`）。",
        {},
        {},
        {"---@return Group group 新卡片集合。"}
    ))

    add("Group.KeepAlive", sig(
        "colon",
        "标记集合跨脚本调用持久存活，避免被引擎自动回收（使用完毕须手动调用 `DeleteGroup` 释放；对只读集合无效）。",
        {},
        {},
        {"---@return nil"}
    ))

    add("Group.DeleteGroup", sig(
        "colon",
        "解除 `KeepAlive` 标记并交回引擎回收（仅对已调用 `KeepAlive` 的集合有效）。",
        {},
        {},
        {"---@return nil"}
    ))

    add("Group.Clone", sig(
        "colon",
        "复制当前集合的全部卡片引用到一个新集合。",
        {},
        {},
        {"---@return Group group 新卡片集合。"}
    ))

    add("Group.FromCards", sig(
        "dot",
        "从 0 个或多个卡片对象构造新集合（自动跳过其中的 `nil` 参数）。",
        {"..."},
        {"---@param ... Card|nil 要加入集合的卡片（`nil` 会被忽略）。"},
        {"---@return Group group 新卡片集合。"}
    ))

    add("Group.Clear", sig(
        "colon",
        "清空集合内所有卡片（只读集合忽略；会重置迭代游标）。",
        {},
        {},
        {"---@return nil"}
    ))

    add("Group.AddCard", sig(
        "colon",
        "向集合插入一张卡片（只读集合忽略；会重置迭代游标）。",
        {"card"},
        {"---@param card Card 要加入的卡片。"},
        {"---@return nil"}
    ))

    add("Group.RemoveCard", sig(
        "colon",
        "从集合移除一张卡片（只读集合忽略；会重置迭代游标）。",
        {"card"},
        {"---@param card Card 要移除的卡片。"},
        {"---@return nil"}
    ))

    add("Group.GetNext", sig(
        "colon",
        "推进迭代游标并返回下一张卡片；必须先调用 `Group:GetFirst()`，否则在集合被修改后调用会抛 Lua 错误。",
        {},
        {},
        {"---@return Card|nil card 下一张卡片；到达末尾时返回 `nil`。"}
    ))

    add("Group.GetFirst", sig(
        "colon",
        "重置迭代游标并返回集合中的第一张卡片。",
        {},
        {},
        {"---@return Card|nil card 第一张卡片；空集合返回 `nil`。"}
    ))

    add("Group.GetCount", sig(
        "colon",
        "取得集合内卡片数量（等价于 `#group`）。",
        {},
        {},
        {"---@return integer count 卡片数量。"}
    ))

    add("Group.__len", sig(
        "colon",
        "长度元方法 `#group`，返回集合内卡片数量。",
        {},
        {},
        {"---@return integer count 卡片数量。"}
    ))

    add("Group.Filter", sig(
        "colon",
        "按过滤函数筛选集合并返回新集合；注意第 2 参 `excluded` 必须显式传入（无排除时传 `nil`）。",
        {"filter", "excluded", "..."},
        {
            "---@param filter FilterFunction 过滤函数 `fun(c: Card, ...): boolean|integer`。",
            "---@param excluded Card|Group|nil 排除的单卡或集合（必填，无排除时显式传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return Group filtered 满足条件的新集合。"}
    ))

    add("Group.FilterCount", sig(
        "colon",
        "统计集合中满足过滤条件的卡片数量；`excluded` 必须显式传入（无排除时传 `nil`）。",
        {"filter", "excluded", "..."},
        {
            "---@param filter FilterFunction 过滤函数。",
            "---@param excluded Card|Group|nil 排除的单卡或集合（必填，无排除时显式传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return integer count 满足条件的卡片数。"}
    ))

    add("Group.FilterSelect", sig(
        "colon",
        "[协程挂起] 按过滤条件让玩家从集合中选择 `min..max` 张卡片。",
        {"player", "filter", "min", "max", "excluded", "..."},
        {
            "---@param player Player 执行选择的玩家编号（0 或 1）。",
            "---@param filter FilterFunction 过滤函数。",
            "---@param min integer 最少选择张数。",
            "---@param max integer 最多选择张数。",
            "---@param excluded Card|Group|nil 排除的单卡或集合（必填，无排除时显式传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return Group|nil selected 选中的卡片集合（`player` 非 0/1 时无返回值）。"}
    ))

    add("Group.Select", sig(
        "colon",
        "[协程挂起] 让玩家从集合中不可取消地选择 `min..max` 张卡片。",
        {"player", "min", "max", "excluded"},
        {
            "---@param player Player 执行选择的玩家编号（0 或 1）。",
            "---@param min integer 最少选择张数。",
            "---@param max integer 最多选择张数。",
            "---@param excluded Card|Group|nil 排除的单卡或集合（必填，无排除时显式传 `nil`）。"
        },
        {"---@return Group|nil selected 选中的卡片集合（`player` 非 0/1 时无返回值）。"},
        {"--- 注意：第 4 参是排除项 `excluded`，不是 `cancelable`！需要可取消选择请改用 `Group:CancelableSelect`。"}
    ))

    add("Group.SelectUnselect", sig(
        "colon",
        "[协程挂起] 在待选集合（`self`）与已选可反选集合（`unselectGroup`）之间交互点选一张卡（常用于多步自定义组合选择）。",
        {"unselectGroup", "player", "finishable", "cancelable", "min", "max"},
        {
            "---@param unselectGroup Group|nil 已选中且允许反选的集合（须与 `self` 无交集，可为 `nil`）。",
            "---@param player Player 执行选择的玩家编号（0 或 1）。",
            "---@param finishable? boolean 是否允许点“完成”结束选择（默认 `false`）。",
            "---@param cancelable? boolean 是否允许点“取消”中止选择（默认 `false`）。",
            "---@param min? integer 显示给客户端的最少数量提示（默认 `1`）。",
            "---@param max? integer 显示给客户端的最多数量提示（默认 `1`）。"
        },
        {"---@return Card|nil card 玩家点击的卡片；点完成/取消或两集合为空/有交集时返回 `nil`。"}
    ))

    add("Group.RandomSelect", sig(
        "colon",
        "从集合中随机抽取最多 `count` 张卡片并向客户端发送 `MSG_RANDOM_SELECTED`。",
        {"player", "count"},
        {
            "---@param player Player 触发随机选择的玩家编号。",
            "---@param count integer 随机选取张数（大于集合大小时自动截断为集合大小）。"
        },
        {"---@return Group selected 随机选中的卡片集合。"}
    ))

    add("Group.CancelableSelect", sig(
        "colon",
        "[协程挂起] 让玩家从集合中可取消地选择 `min..max` 张卡片；玩家取消时返回 `nil`。",
        {"player", "min", "max", "excluded"},
        {
            "---@param player Player 执行选择的玩家编号（0 或 1）。",
            "---@param min integer 最少选择张数。",
            "---@param max integer 最多选择张数。",
            "---@param excluded Card|Group|nil 排除的单卡或集合（必填，无排除时显式传 `nil`）。"
        },
        {"---@return Group|nil selected 选中的卡片集合；玩家取消或 `player` 非 0/1 时返回 `nil`。"}
    ))

    add("Group.IsExists", sig(
        "colon",
        "检查集合中是否存在至少 `count` 张满足过滤条件的卡片；`excluded` 必须显式传入（无排除时传 `nil`）。",
        {"filter", "count", "excluded", "..."},
        {
            "---@param filter FilterFunction 过滤函数。",
            "---@param count integer 所需最少张数。",
            "---@param excluded Card|Group|nil 排除的单卡或集合（必填，无排除时显式传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return boolean exists 是否满足数量条件。"}
    ))

    add("Group.CheckWithSumEqual", sig(
        "colon",
        "检查集合（并计入 `Duel.SetSelectedCard` 的必选卡）中能否选出 `min..max` 张卡使求值函数总和恰好等于 `acc`（调用后会清空 `must_select_cards`）。",
        {"valueFunc", "acc", "min", "max", "..."},
        {
            "---@param valueFunc SumValueFunction 卡片求值函数（支持高低 16 位双值编码）。",
            "---@param acc integer 目标总和。",
            "---@param min integer 最少选取张数。",
            "---@param max integer 最多选取张数。",
            "---@param ... any 透传给 `valueFunc` 的附加参数。"
        },
        {"---@return boolean possible 是否存在合法组合。"}
    ))

    add("Group.SelectWithSumEqual", sig(
        "colon",
        "[协程挂起] 让玩家从集合中选出 `min..max` 张卡（含必选卡）使总和恰好等于 `acc`（`max` 不得超过 255；无合法解时直接返回空集合）。",
        {"player", "valueFunc", "acc", "min", "max", "..."},
        {
            "---@param player Player 执行选择的玩家编号（0 或 1）。",
            "---@param valueFunc SumValueFunction 卡片求值函数。",
            "---@param acc integer 目标总和。",
            "---@param min integer 最少选取张数。",
            "---@param max integer 最多选取张数（上限 255）。",
            "---@param ... any 透传给 `valueFunc` 的附加参数。"
        },
        {"---@return Group|nil selected 选中的卡片集合（不含先前置入的 `must_select_cards`；`player` 非 0/1 无返回值）。"}
    ))

    add("Group.CheckWithSumGreater", sig(
        "colon",
        "检查集合（含必选卡）中是否存在最小超额组合使求值总和大于等于 `acc`（调用后会清空 `must_select_cards`）。",
        {"valueFunc", "acc", "..."},
        {
            "---@param valueFunc SumValueFunction 卡片求值函数。",
            "---@param acc integer 目标下限总和。",
            "---@param ... any 透传给 `valueFunc` 的附加参数。"
        },
        {"---@return boolean possible 是否存在合法组合。"}
    ))

    add("Group.SelectWithSumGreater", sig(
        "colon",
        "[协程挂起] 让玩家从集合中选出最小超额组合使求值总和大于等于 `acc`（无合法解时返回空集合）。",
        {"player", "valueFunc", "acc", "..."},
        {
            "---@param player Player 执行选择的玩家编号（0 或 1）。",
            "---@param valueFunc SumValueFunction 卡片求值函数。",
            "---@param acc integer 目标下限总和。",
            "---@param ... any 透传给 `valueFunc` 的附加参数。"
        },
        {"---@return Group|nil selected 选中的卡片集合（`player` 非 0/1 无返回值）。"}
    ))

    add("Group.GetMinGroup", sig(
        "colon",
        "按求值函数找出集合中取值最小的所有卡片集合及对应的最小值（双返回值；空集合无返回值）。",
        {"valueFunc", "..."},
        {
            "---@param valueFunc SumValueFunction 卡片整型求值函数。",
            "---@param ... any 透传给 `valueFunc` 的附加参数。"
        },
        {
            "---@return Group|nil minGroup 取得最小值的卡片集合（空集合返回 `nil`）。",
            "---@return integer|nil minValue 最小数值。"
        }
    ))

    add("Group.GetMaxGroup", sig(
        "colon",
        "按求值函数找出集合中取值最大的所有卡片集合及对应的最大值（双返回值；空集合无返回值）。",
        {"valueFunc", "..."},
        {
            "---@param valueFunc SumValueFunction 卡片整型求值函数。",
            "---@param ... any 透传给 `valueFunc` 的附加参数。"
        },
        {
            "---@return Group|nil maxGroup 取得最大值的卡片集合（空集合返回 `nil`）。",
            "---@return integer|nil maxValue 最大数值。"
        }
    ))

    add("Group.GetSum", sig(
        "colon",
        "对集合内每张卡调用求值函数并累加返回总和。",
        {"valueFunc", "..."},
        {
            "---@param valueFunc SumValueFunction 卡片整型求值函数（必须为函数）。",
            "---@param ... any 透传给 `valueFunc` 的附加参数。"
        },
        {"---@return integer sum 总和。"}
    ))

    add("Group.GetClassCount", sig(
        "colon",
        "统计集合内卡片经整型求值函数映射后的不同取值个数（如统计不同卡名/不同等级数）。",
        {"classFunc", "..."},
        {
            "---@param classFunc SumValueFunction 分类整型求值函数（如 `Card.GetCode`、`Card.GetLevel`）。",
            "---@param ... any 透传给 `classFunc` 的附加参数。"
        },
        {"---@return integer count 不同分类的数量。"}
    ))

    add("Group.Remove", sig(
        "colon",
        "原地从集合中移除所有满足过滤条件的卡片（只读集合忽略）；注意 `excluded` 仅接受 `Card|nil`（不接受 `Group`）。",
        {"filter", "excluded", "..."},
        {
            "---@param filter FilterFunction 过滤函数。",
            "---@param excluded Card|nil 排除的单张卡片（必填，无排除时显式传 `nil`；不接受 `Group`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return nil"}
    ))

    add("Group.Merge", sig(
        "colon",
        "原地将 `other` 集合中的全部卡片并入当前集合（只读集合忽略；会重置迭代游标）。",
        {"other"},
        {"---@param other Group 要合并进来的集合。"},
        {"---@return nil"}
    ))

    add("Group.Sub", sig(
        "colon",
        "原地从当前集合中减去 `other` 集合包含的全部卡片（只读集合忽略；会重置迭代游标）。",
        {"other"},
        {"---@param other Group 要减去的集合。"},
        {"---@return nil"}
    ))

    add("Group.Equal", sig(
        "colon",
        "比较两个集合包含的卡片是否完全一致。",
        {"other"},
        {"---@param other Group 要比较的集合。"},
        {"---@return boolean equal"}
    ))

    add("Group.IsContains", sig(
        "colon",
        "检查当前集合是否包含指定卡片。",
        {"card"},
        {"---@param card Card 要检查的卡片。"},
        {"---@return boolean contains"}
    ))

    add("Group.SearchCard", sig(
        "colon",
        "在集合中查找第一张满足过滤函数的卡片；未找到时无返回值（`nil`）。",
        {"filter", "..."},
        {
            "---@param filter FilterFunction 过滤函数。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return Card|nil card 命中的第一张卡片；未找到返回 `nil`。"}
    ))

    add("Group.GetBinClassCount", sig(
        "colon",
        "对集合内每张卡的整型求值结果按位或合并后统计置位比特数（popcount，常用于统计不同属性/种族数）。",
        {"bitFunc", "..."},
        {
            "---@param bitFunc SumValueFunction 返回位掩码的函数（如 `Card.GetAttribute`、`Card.GetRace`）。",
            "---@param ... any 透传给 `bitFunc` 的附加参数。"
        },
        {"---@return integer bitCount 合并后位掩码中 1 的个数。"}
    ))

    add("Group.__add", sig(
        "dot",
        "并集元方法 `a + b`：返回包含 `a` 与 `b` 全部卡片的新集合。",
        {"lhs", "rhs"},
        {
            "---@param lhs Card|Group 左操作数。",
            "---@param rhs Card|Group 右操作数。"
        },
        {"---@return Group union 新并集集合。"}
    ))

    add("Group.__bor", sig(
        "dot",
        "并集元方法 `a | b`（与 `a + b` 共享同一 C++ 实现）：返回包含 `a` 与 `b` 全部卡片的新集合。",
        {"lhs", "rhs"},
        {
            "---@param lhs Card|Group 左操作数。",
            "---@param rhs Card|Group 右操作数。"
        },
        {"---@return Group union 新并集集合。"}
    ))

    add("Group.__sub", sig(
        "dot",
        "差集元方法 `a - b`：返回属于 `a` 但不属于 `b` 的新集合。",
        {"lhs", "rhs"},
        {
            "---@param lhs Card|Group 左操作数。",
            "---@param rhs Card|Group 右操作数。"
        },
        {"---@return Group diff 新差集集合。"}
    ))

    add("Group.__band", sig(
        "dot",
        "交集元方法 `a & b`：返回同时属于 `a` 和 `b` 的新集合。",
        {"lhs", "rhs"},
        {
            "---@param lhs Card|Group 左操作数。",
            "---@param rhs Card|Group 右操作数。"
        },
        {"---@return Group intersection 新交集集合。"}
    ))

    add("Group.__bxor", sig(
        "dot",
        "对称差集元方法 `a ~ b`：返回仅属于 `a` 或仅属于 `b`（不同时属于两者）的新集合。",
        {"lhs", "rhs"},
        {
            "---@param lhs Card|Group 左操作数。",
            "---@param rhs Card|Group 右操作数。"
        },
        {"---@return Group symDiff 新对称差集集合。"}
    ))
end

return { register = register }
