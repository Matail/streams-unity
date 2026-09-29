// UGUI 를 코드로 만드는 작은 도우미. 1단계는 씬·프리팹 없이 코드만으로 화면을 띄운다
// (리포에 씬 YAML 을 손으로 쓰지 않기 위해). 연출·아트가 들어오면 프리팹으로 옮긴다.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Streams.Presentation
{
    static class Ui
    {
        // 사이트 STREAMS 와 같은 계열: 거의 검정 배경 + 시안 강조
        public static readonly Color Bg = new Color32(0x0E, 0x10, 0x14, 0xFF);
        public static readonly Color Panel = new Color32(0x17, 0x1A, 0x21, 0xFF);
        public static readonly Color Slot = new Color32(0x22, 0x26, 0x30, 0xFF);
        public static readonly Color SlotFilled = new Color32(0x2C, 0x3A, 0x48, 0xFF);
        public static readonly Color Accent = new Color32(0x57, 0xC7, 0xF5, 0xFF);
        public static readonly Color Ink = new Color32(0xE8, 0xEA, 0xEE, 0xFF);
        public static readonly Color Muted = new Color32(0x8A, 0x90, 0x9C, 0xFF);

        static Font font;
        public static Font Font => font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Canvas Canvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;

            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                es.AddComponent<StandaloneInputModule>();
#endif
            }
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Box(string name, Transform parent, Color color)
        {
            var img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var t = Rect(name, parent).gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button Button(string name, Transform parent, string text, int size, System.Action onClick)
        {
            var img = Box(name, parent, Slot);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.disabledColor = new Color(0.8f, 0.8f, 0.8f);
            b.colors = colors;
            var label = Label("Label", img.transform, text, size, Ink);
            Fill(label.rectTransform);
            b.onClick.AddListener(() => onClick());
            return b;
        }

        public static void Size(Component c, float w, float h)
        {
            if (!c.TryGetComponent<LayoutElement>(out var le)) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = h;
        }

        public static T Layout<T>(RectTransform rt, float spacing, TextAnchor align = TextAnchor.MiddleCenter) where T : HorizontalOrVerticalLayoutGroup
        {
            var g = rt.gameObject.AddComponent<T>();
            g.spacing = spacing;
            g.childAlignment = align;
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = g.childForceExpandHeight = false;
            return g;
        }
    }
}
