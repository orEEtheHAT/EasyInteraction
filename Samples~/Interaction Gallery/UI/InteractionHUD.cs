using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace EasyInteraction.UI
{
    public enum InteractionMenuLayout
    {
        List,
        Radial
    }

    [DisallowMultipleComponent, RequireComponent(typeof(InteractionPresenter)), AddComponentMenu("Easy Interaction/UI/Interaction HUD")]
    public sealed class InteractionHUD : MonoBehaviour, IInteractionLocalView
    {
        [SerializeField]
        private InteractionMenuLayout layout;
        [SerializeField]
        private Color accent = new Color(.25f, .75f, 1);
        [SerializeField, Range(.75f, 2)]
        private float scale = 1;
        private InteractionPresenter presenter;
        private GameObject canvasObject;
        private RectTransform panel, menu;
        private Text title, prompt, reason;
        private Image progress;
        private GameObject pager;
        private Text pageLabel;
        private Button previousPage, nextPage;
        private int pageIndex, lastPageIndex = -1, lastPageCount = -1;
        private InteractionTarget pagedTarget;
        private readonly List<Row> rows = new List<Row>(8);
        private readonly List<InteractionAction> offered = new List<InteractionAction>(8);
        private bool menuOpen;
        private InputAction openMenu;
        private EventSystem ownedEventSystem;
        private static EventSystem sharedEventSystem;
        private static int sharedUsers;
        private float refreshAt;
        private string lastLabel, lastBinding;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private sealed class Row
        {
            public GameObject Root;
            public Text Text;
            public Image Background, Icon;
            public InteractionButton Input;
            public InteractionAction Action;
            public InteractionMenuLayout Layout;
        }

        public InteractionMenuLayout Layout
        {
            get => layout;
            set
            {
                layout = value;
                pageIndex = 0;
                RefreshRows();
            }
        }

        public bool MenuOpen => menuOpen;
        private int PageSize => layout == InteractionMenuLayout.List ? 5 : 6;
        public int ActionCount => offered.Count;
        public int PageCount => Mathf.Max(1, (offered.Count + PageSize - 1) / PageSize);
        public int PageIndex => pageIndex;
        public int VisibleActionCount => Mathf.Min(PageSize, Mathf.Max(0, offered.Count - pageIndex * PageSize));

        public void SetPage(int index)
        {
            pageIndex = Mathf.Clamp(index, 0, PageCount - 1);
            RefreshRows();
            if (menuOpen && VisibleActionCount > 0)
            {
                presenter.Actor.SelectAction(rows[0].Action);
                if (EventSystem.current)
                    EventSystem.current.SetSelectedGameObject(rows[0].Root);
            }
        }

        public void SetLocalActive(bool local) => enabled = local;
        private static Font font;
        private static Font Font => font ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        private void OnEnable()
        {
            presenter = GetComponent<InteractionPresenter>();
            Build();
            presenter.Changed += Changed;
            openMenu = new InputAction("InteractionMenu", InputActionType.Button, "<Keyboard>/c");
            openMenu.AddBinding("<Gamepad>/select");
            openMenu.performed += Toggle;
            openMenu.Enable();
            presenter.Refresh();
            Changed(presenter.Prompt);
        }

        private void OnDisable()
        {
            if (menuOpen)
                SetMenuOpen(false);
            if (presenter)
                presenter.Changed -= Changed;
            if (openMenu != null)
            {
                openMenu.performed -= Toggle;
                openMenu.Dispose();
                openMenu = null;
            }

            if (canvasObject)
                Destroy(canvasObject);
            ReleaseEventSystem();
            rows.Clear();
            offered.Clear();
            menuOpen = false;
        }

        private void Toggle(InputAction.CallbackContext _) => SetMenuOpen(!menuOpen);
        public void SetMenuOpen(bool value)
        {
            if (menuOpen == value)
                return;
            menuOpen = value;
            if (value)
            {
                previousCursorLock = Cursor.lockState;
                previousCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                presenter.Actor.PinTarget();
            }
            else
            {
                Cursor.lockState = previousCursorLock;
                Cursor.visible = previousCursorVisible;
                if (presenter)
                    presenter.Actor.ReleaseTargetLock();
            }

            if (menu)
                menu.gameObject.SetActive(value);
            RefreshRows();
            if (value && rows.Count > 0 && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(rows[0].Root);
        }

        private void LateUpdate()
        {
            if (!canvasObject)
            {
                rows.Clear();
                Build();
                Changed(presenter.Prompt);
            }

            if (Time.unscaledTime < refreshAt)
                return;
            refreshAt = Time.unscaledTime + .1f;
            EnsureEventSystem();
            RefreshRows();
        }

        private void Build()
        {
            lastLabel = lastBinding = null;
            canvasObject = new GameObject("Interaction HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = .5f;
            panel = Rect("Prompt", canvasObject.transform, new Vector2(0, -225), new Vector2(500, 128));
            panel.localScale = Vector3.one * scale;
            panel.gameObject.AddComponent<Image>().color = new Color(.035f, .045f, .065f, .92f);
            title = Label("Target", panel, new Vector2(0, 38), new Vector2(470, 26), 18);
            prompt = Label("Action", panel, new Vector2(0, 10), new Vector2(470, 28), 22);
            prompt.color = accent;
            reason = Label("Reason", panel, new Vector2(0, -23), new Vector2(470, 38), 14);
            var bar = Rect("Progress", panel, new Vector2(0, -59), new Vector2(470, 4));
            bar.gameObject.AddComponent<Image>().color = new Color(.15f, .18f, .22f);
            progress = Rect("Fill", bar, Vector2.zero, new Vector2(470, 4)).gameObject.AddComponent<Image>();
            progress.color = accent;
            progress.rectTransform.pivot = new Vector2(0, .5f);
            progress.rectTransform.anchorMin = progress.rectTransform.anchorMax = new Vector2(0, .5f);
            progress.rectTransform.anchoredPosition = Vector2.zero;
            menu = Rect("Actions", canvasObject.transform, new Vector2(0, 65), new Vector2(550, 340));
            pager = Rect("Pages", menu, new Vector2(0, 235), new Vector2(420, 32)).gameObject;
            pageLabel = Label("Page", pager.transform, Vector2.zero, new Vector2(120, 30), 15);
            previousPage = PageButton("Previous", new Vector2(-150, 0));
            previousPage.onClick.AddListener(() => SetPage(pageIndex - 1));
            nextPage = PageButton("Next", new Vector2(150, 0));
            nextPage.onClick.AddListener(() => SetPage(pageIndex + 1));
            lastPageIndex = lastPageCount = -1;
            menu.gameObject.SetActive(false);
            EnsureEventSystem();
        }

        private void EnsureEventSystem()
        {
            if (!EventSystem.current)
            {
                var system = new GameObject("Interaction UI EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                sharedEventSystem = system.GetComponent<EventSystem>();
                sharedUsers = 0;
                system.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            if (EventSystem.current == sharedEventSystem && !ReferenceEquals(ownedEventSystem, sharedEventSystem))
            {
                ReleaseEventSystem();
                ownedEventSystem = sharedEventSystem;
                sharedUsers++;
            }
        }

        private void ReleaseEventSystem()
        {
            if (!ReferenceEquals(ownedEventSystem, null) && ReferenceEquals(ownedEventSystem, sharedEventSystem))
            {
                if (--sharedUsers <= 0)
                {
                    if (sharedEventSystem)
                    {
                        sharedEventSystem.gameObject.SetActive(false);
                        Destroy(sharedEventSystem.gameObject);
                    }

                    sharedEventSystem = null;
                    sharedUsers = 0;
                }
            }

            ownedEventSystem = null;
        }

        private void Changed(InteractionPrompt data)
        {
            if (!panel)
                return;
            panel.gameObject.SetActive(data.Action);
            title.text = data.TargetName;
            if (lastLabel != data.Label || lastBinding != data.Binding)
            {
                lastLabel = data.Label;
                lastBinding = data.Binding;
                prompt.text = string.IsNullOrEmpty(data.Binding) ? data.Label : "[" + data.Binding + "] " + data.Label;
            }

            reason.text = data.Available ? (data.Running ? presenter.Translate("ui.running", "Q: cancel · C: actions") : presenter.Translate("ui.actions", "Tab: next · F: secondary · C: actions")) : data.Reason;
            progress.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 470 * Mathf.Clamp01(data.Progress));
            RefreshRows();
        }

        private void RefreshRows()
        {
            if (!presenter || !menu)
                return;
            if (pagedTarget != presenter.Actor.CurrentTarget)
            {
                pagedTarget = presenter.Actor.CurrentTarget;
                pageIndex = 0;
            }

            offered.Clear();
            var actions = presenter.Actor.Actions;
            for (int index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                if (!action || (presenter.Actor.CurrentPoint && !presenter.Actor.CurrentPoint.Allows(action)))
                    continue;
                var check = presenter.Check(action);
                if (action.HideWhenUnavailable && !check.IsAvailable)
                    continue;
                offered.Add(action);
            }

            pageIndex = Mathf.Clamp(pageIndex, 0, PageCount - 1);
            int visible = VisibleActionCount;
            pager.SetActive(PageCount > 1);
            if (lastPageIndex != pageIndex || lastPageCount != PageCount)
            {
                lastPageIndex = pageIndex;
                lastPageCount = PageCount;
                pageLabel.text = (pageIndex + 1) + " / " + PageCount;
                previousPage.interactable = pageIndex > 0;
                nextPage.interactable = pageIndex + 1 < PageCount;
            }

            while (rows.Count < visible)
                CreateRow();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                row.Root.SetActive(i < visible);
                if (i >= visible)
                    continue;
                row.Action = offered[pageIndex * PageSize + i];
                row.Input.Action = row.Action;
                var check = presenter.Check(row.Action);
                row.Input.Available = check.IsAvailable;
                row.Input.interactable = check.IsAvailable;
                row.Icon.sprite = row.Action.Icon;
                row.Icon.gameObject.SetActive(row.Action.Icon);
                string label = presenter.Translate(row.Action.LabelKey, row.Action.GetLabel(presenter.Actor));
                // Strings are rebuilt only when UI content changes; steady-state refresh reuses the previous text.
                string denial = check.IsAvailable ? "" : presenter.Translate(check.TextKey, check.Reason);
                if (row.Input.Label != label || row.Input.Reason != denial || row.Layout != layout)
                {
                    row.Layout = layout;
                    row.Input.Label = label;
                    row.Input.Reason = denial;
                    row.Text.text = check.IsAvailable || layout == InteractionMenuLayout.Radial ? label : label + "\n" + denial;
                }

                row.Background.color = check.IsAvailable ? new Color(.08f, .12f, .18f, .97f) : new Color(.1f, .1f, .11f, .94f);
            }

            Arrange();
        }

        private void CreateRow()
        {
            var rect = Rect("Action", menu, Vector2.zero, new Vector2(420, 52));
            var background = rect.gameObject.AddComponent<Image>();
            var text = Label("Label", rect, Vector2.zero, new Vector2(400, 48), 16);
            var input = rect.gameObject.AddComponent<InteractionButton>();
            input.Presenter = presenter;
            input.targetGraphic = background;
            var icon = Rect("Icon", rect, new Vector2(-175, 0), new Vector2(24, 24)).gameObject.AddComponent<Image>();
            icon.raycastTarget = false;
            rows.Add(new Row { Root = rect.gameObject, Text = text, Background = background, Icon = icon, Input = input });
        }

        private void Arrange()
        {
            int count = VisibleActionCount;
            for (int i = 0; i < count && i < rows.Count; i++)
            {
                var rect = (RectTransform)rows[i].Root.transform;
                if (layout == InteractionMenuLayout.List)
                {
                    rect.anchoredPosition = new Vector2(0, (count - 1) * 29 - i * 58);
                    rect.sizeDelta = new Vector2(420, 52);
                }
                else
                {
                    float angle = (90 - i * 360f / Mathf.Max(1, count)) * Mathf.Deg2Rad;
                    rect.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 175;
                    rect.sizeDelta = new Vector2(210, 62);
                }

                rows[i].Text.rectTransform.sizeDelta = rect.sizeDelta - new Vector2(12, 4);
                rows[i].Icon.rectTransform.anchoredPosition = new Vector2(-rect.sizeDelta.x * .5f + 20, 0);
            }
        }

        private Button PageButton(string caption, Vector2 position)
        {
            var rect = Rect(caption, pager.transform, position, new Vector2(130, 30));
            rect.gameObject.AddComponent<Image>().color = new Color(.08f, .12f, .18f, .97f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            Label("Label", rect, Vector2.zero, new Vector2(126, 28), 15).text = presenter.Translate("ui." + caption.ToLowerInvariant(), caption);
            return button;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Text Label(string name, Transform parent, Vector2 position, Vector2 size, int fontSize)
        {
            var text = Rect(name, parent, position, size).gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }

    public sealed class InteractionButton : Selectable, ISubmitHandler
    {
        public InteractionPresenter Presenter;
        private InteractionAction action;
        public InteractionAction Action
        {
            get => action;
            set
            {
                if (action == value)
                    return;
                ReleasePress();
                action = value;
            }
        }

        public bool Available;
        internal string Label, Reason;
        private InteractionTicket pressed;
        private bool pressedHere;
        public override void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            base.OnPointerDown(eventData);
            if (!Available || !Presenter)
                return;
            Presenter.Activate(Action);
            pressedHere = true;
            pressed = Presenter.Actor.GetTicket(Action);
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
            ReleasePress();
        }

        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            if (Presenter && Action)
                Presenter.Actor.SelectAction(Action);
        }

        protected override void OnDisable()
        {
            ReleasePress();
            base.OnDisable();
        }

        private void ReleasePress()
        {
            if (pressedHere && Presenter)
            {
                if (Presenter.Actor.CommandAdapter)
                    Presenter.Release(Action);
                else if (pressed.IsActive)
                    Presenter.Release(pressed.Action);
            }

            pressedHere = false;
            pressed = default;
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (Available && Presenter)
                Presenter.Actor.Press(Action, true);
        }
    }
}
