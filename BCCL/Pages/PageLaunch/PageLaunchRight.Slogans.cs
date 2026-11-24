using System.Windows.Controls;
using Microsoft.VisualBasic;
using BCCL.Core.App;
using BCCL.Core.App.Localization;

namespace BCCL;

public partial class PageLaunchRight
{
    private static readonly (string First, string Second)[] _WorkspaceSlogans =
    [
        ("你的世界", "即刻启程"),
        ("方块无界", "探索有方"),
        ("一镐在手", "百矿相逢"),
        ("木斧开荒", "石镐探矿"),
        ("日出播种", "月下归家"),
        ("穿过雪原", "点亮木屋"),
        ("一方天地", "万种玩法"),
        ("昼寻村落", "夜守篝火"),
        ("循光向前", "遇水搭桥"),
        ("背上行囊", "翻过群山"),
        ("凿石成路", "引水成田"),
        ("推门见海", "抬眼见星"),
        ("红石轻响", "机关缓启"),
        ("穿林寻狐", "渡海觅岛"),
        ("手植橡树", "亲筑小屋"),
        ("翻越高岭", "走进深谷"),
        ("矿脉藏光", "石壁留影"),
        ("误入花海", "偶遇村庄"),
        ("风过麦田", "雨落屋檐"),
        ("架桥越谷", "搭梯攀峰"),
        ("回程有灯", "入夜有床"),
        ("点亮火把", "照见前路"),
        ("循着坐标", "找回家门"),
        ("蘑菇成林", "橡木成屋"),
        ("雪球作伴", "雪人看家"),
        ("玻璃映海", "灯火照岸"),
        ("驯马赶路", "乘船远行"),
        ("小麦摇曳", "蜜蜂归巢"),
        ("绿林深处", "另有天地"),
        ("旷野为邻", "山川作伴"),
        ("竹影深处", "熊猫打盹"),
        ("白桦映雪", "云杉迎风"),
        ("门后下界", "脚下熔岩"),
        ("远眺末地", "振翅飞行"),
        ("今日挖矿", "明日筑城"),
        ("背包满满", "满载而归"),
        ("坐看潮落", "静待日出"),
        ("开垦荒地", "种出春天"),
        ("村钟响起", "村民归屋"),
        ("背包有粮", "远行有伴"),
        ("脚踩砂砾", "眼望绿洲"),
        ("越过冰川", "驶入暖洋"),
        ("海边垂钓", "炉边烤鱼"),
        ("黑曜筑门", "火星启程"),
        ("砌起围墙", "挂上灯笼"),
        ("浮岛在上", "云海在下"),
        ("轨道延伸", "矿车出发"),
        ("千格之外", "仍有风景"),
        ("南瓜点灯", "西瓜解暑"),
        ("拂晓整装", "黄昏落脚"),
        ("门前种花", "屋后种瓜"),
        ("风吹细沙", "月照古塔"),
        ("羊群入圈", "小狗守门"),
        ("洞穴寻路", "晶洞寻光"),
        ("踏遍群系", "画满地图"),
        ("从树开始", "向星出发"),
    ];

    private int _workspaceSloganIndex;

    private void InitializeWorkspaceSlogan()
    {
        _workspaceSloganIndex = Random.Shared.Next(_WorkspaceSlogans.Length);
        if (_workspaceSloganIndex == States.UI.WorkbenchLastSlogan)
        {
            _workspaceSloganIndex = (_workspaceSloganIndex + Random.Shared.Next(1, _WorkspaceSlogans.Length))
                % _WorkspaceSlogans.Length;
        }

        States.UI.WorkbenchLastSlogan = _workspaceSloganIndex;
        UpdateWorkspaceSlogan();
        WeakLanguageChanged.Add(this, OnWorkspaceSloganLanguageChanged);
    }

    private static void OnWorkspaceSloganLanguageChanged(PageLaunchRight page) => ModBase.RunInUi(page.UpdateWorkspaceSlogan);

    private void UpdateWorkspaceSlogan()
    {
        var language = LocalizationService.CurrentLanguage.Code;
        if (language is not ("zh-CN" or "zh-TW"))
        {
            LabWorkspaceSlogan.SetResourceReference(TextBlock.TextProperty, "Workbench.Hero");
            return;
        }

        var (first, second) = _WorkspaceSlogans[_workspaceSloganIndex];
        var slogan = $"{first}，\n{second}。";
        LabWorkspaceSlogan.Text = language == "zh-TW"
            ? Strings.StrConv(slogan, VbStrConv.TraditionalChinese, 0x0804)
            : slogan;
    }
}
