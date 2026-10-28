using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    public sealed class ModeSelectController : MonoBehaviour, IFrontendPageController
    {
        private FrontendPageContext _context = null!;
        public void InjectPageContext(FrontendPageContext context) => _context = context;

        private void OnEnable()
        {
            MatchConfig.ClearActiveMatch();
            var style = Resources.Load<StyleSheet>("UI/LocalPlay");
            foreach (var root in _context.OwnedRoots) if (style != null) root.styleSheets.Add(style);
            var hero = _context.Q<VisualElement>("local-mode-hero")!;
            var stack = _context.Q<VisualElement>("local-mode-stack")!;
            Button? initial = null;
            foreach (var mode in LocalPlayModes.All)
            {
                var card = new Button(() => LocalPlayModes.Open(mode.Mode)) { name = "local-mode-" + mode.Mode };
                card.AddToClassList("local-mode-card");
                if (mode.HasStageStep) card.AddToClassList("local-mode-card--hero");
                var art = new VisualElement { pickingMode = PickingMode.Ignore };
                art.AddToClassList("local-mode-art"); art.AddToClassList(mode.PosterClass);
                if (mode.HasStageStep)
                {
                    var badge = new Label("DEFAULT"); badge.AddToClassList("local-mode-badge"); art.Add(badge);
                }
                var caption = new VisualElement { pickingMode = PickingMode.Ignore };
                caption.AddToClassList("local-mode-caption");
                var title = new Label(mode.Title); title.AddToClassList("local-mode-title");
                var rule = new Label(mode.Rule); rule.AddToClassList("local-mode-rule");
                var chips = new Label(mode.Chips); chips.AddToClassList("local-mode-chips");
                caption.Add(title); caption.Add(rule); caption.Add(chips);
                card.Add(art); card.Add(caption);
                card.RegisterCallback<FocusInEvent>(_ => Preview(mode));
                card.RegisterCallback<MouseEnterEvent>(_ => { if (card.enabledSelf) Preview(mode); });
                card.SetEnabled(LocalChallengeSetup.Available(mode));
                card.EnableInClassList("local-mode-card--selected", mode.Mode == MatchConfig.LocalMode);
                (mode.HasStageStep ? hero : stack).Add(card);
                if (mode.Mode == MatchConfig.LocalMode && card.enabledSelf) initial = card;
            }
            var back = _context.Q<Button>("btn-back")!;
            Action leave = () => FrontendController.Show(FrontendPage.Home);
            back.clicked += leave;
            Preview(LocalPlayModes.Current);
            MenuNavigation.Configure(_context, initial ?? hero.Children().OfType<Button>().FirstOrDefault(b => b.enabledSelf) ?? back, leave);
        }

        private void Preview(LocalPlayModeInfo mode)
        {
            foreach (var item in LocalPlayModes.All)
                _context.Q<Button>("local-mode-" + item.Mode)?.EnableInClassList("local-mode-card--selected", mode.Mode == item.Mode);
            _context.Q<Label>("flow-progress")!.text = $"STEP 1 OF {mode.TotalSteps}  /  MODE";
        }
    }
}
