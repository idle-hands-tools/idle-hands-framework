using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IdleHands.UnityIntegration
{
    public sealed class CookiePresenter
    {
        private readonly Dictionary<string, Text> resources = new Dictionary<string, Text>();
        private readonly Dictionary<string, Button> producers = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> upgrades = new Dictionary<string, Button>();
        private readonly Dictionary<string, Text> rules = new Dictionary<string, Text>();
        private readonly Font font;
        private readonly Transform rows;
        private readonly Text status;
        private readonly Text offline;
        private readonly Button prestige;

        public CookiePresenter(Transform parent, GameContentAsset content, Action click,
            Action<string> buyProducer, Action<string> buyUpgrade, Action doPrestige, Action save, Action load)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasObject = new GameObject("CookieDemo UI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1000, 850);
            scaler.matchWidthOrHeight = 0.5f;
            var background = Element("Background", canvasObject.transform, typeof(Image));
            Stretch(background);
            background.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.14f);
            var scrollObject = Element("Scroll", background, typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            Stretch(scrollObject);
            scrollObject.offsetMin = new Vector2(24, 20);
            scrollObject.offsetMax = new Vector2(-24, -20);
            scrollObject.GetComponent<Image>().color = Color.clear;
            var contentRect = Element("Rows", scrollObject, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = Vector2.zero;
            var layout = contentRect.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 9;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            contentRect.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.viewport = scrollObject;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            rows = contentRect;
            Label("IDLE HANDS  /  COOKIE BAKERY", 30, 52);
            Label("Bake cookies, hire cursors, unlock ovens, then prestige. Scroll for more.", 17, 40);
            offline = Label("Offline progress cap: " + content.maximumOfflineSeconds.ToString("F0")
                + " seconds. Save your bakery to resume later.", 17, 48);
            foreach (var resource in content.resources)
            {
                if (resource == content.clickResource)
                {
                    var button = Button("Bake " + resource.displayName, click);
                    resources.Add(resource.id, button.GetComponentInChildren<Text>());
                }
                else resources.Add(resource.id, Label(resource.displayName, 22, 54));
            }
            Label("PRODUCERS", 20, 34);
            foreach (var producer in content.producers)
            {
                string id = producer.id;
                producers.Add(id, Button(producer.displayName, () => buyProducer(id)));
            }
            Label("UPGRADES", 20, 34);
            foreach (var upgrade in content.upgrades)
            {
                string id = upgrade.id;
                upgrades.Add(id, Button(upgrade.displayName, () => buyUpgrade(id)));
            }
            Label("MILESTONES", 20, 34);
            foreach (var rule in content.rules) rules.Add(rule.id, Label(rule.displayName, 18, 38));
            prestige = Button("Prestige", doPrestige);
            Button("Save bakery", save);
            Button("Load saved bakery", load);
            status = Label("Ready", 17, 48);
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            {
                var events = new GameObject("CookieDemo EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(parent, false);
            }
        }

        public void Resource(string id, string text) { resources[id].text = text; }
        public void Producer(string id, string text, bool enabled) { Set(producers[id], text, enabled); }
        public void Upgrade(string id, string text, bool enabled) { Set(upgrades[id], text, enabled); }
        public void Rule(string id, string text) { rules[id].text = text; }
        public void Prestige(string text, bool enabled) { Set(prestige, text, enabled); }
        public void Status(string text) { status.text = text; }
        public void Offline(string text) { offline.text = text; }

        private static void Set(Button button, string text, bool enabled)
        {
            button.GetComponentInChildren<Text>().text = text;
            button.interactable = enabled;
        }

        private Text Label(string text, int size, float height)
        {
            var rect = Element("Label", rows, typeof(Text), typeof(LayoutElement));
            rect.GetComponent<LayoutElement>().preferredHeight = height;
            var label = rect.GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = new Color(0.94f, 0.94f, 0.98f);
            label.alignment = TextAnchor.MiddleLeft;
            label.text = text;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }

        private Button Button(string title, Action action)
        {
            var rect = Element(title, rows, typeof(Image), typeof(Button), typeof(LayoutElement));
            rect.GetComponent<LayoutElement>().preferredHeight = 58;
            rect.GetComponent<Image>().color = new Color(0.19f, 0.28f, 0.43f);
            var button = rect.GetComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors;
            colors.highlightedColor = new Color(0.8f, 0.9f, 1);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.5f, 0.75f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            var text = Element("Text", rect, typeof(Text));
            Stretch(text);
            text.offsetMin = new Vector2(14, 0);
            text.offsetMax = new Vector2(-14, 0);
            var label = text.GetComponent<Text>();
            label.font = font;
            label.fontSize = 20;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            label.text = title;
            label.raycastTarget = false;
            return button;
        }

        private static RectTransform Element(string name, Transform parent, params Type[] components)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            foreach (var type in components) gameObject.AddComponent(type);
            return gameObject.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
