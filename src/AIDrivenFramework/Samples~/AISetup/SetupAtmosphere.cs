using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Small, original procedural textures. No camera, shader, downloaded art or per-frame blur.</summary>
sealed class SetupAtmosphere : IDisposable
{
    const int Size = 192;
    readonly Texture2D backdrop, frost;
    public readonly Texture2D Accent, Icon;
    readonly Layer background, glass;
    float lastFrame;
    Vector2 offset;
    public Vector2 Offset => offset;

    public SetupAtmosphere(VisualElement root, VisualElement card)
    {
        var pixels = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float u = x / (float)(Size - 1), v = y / (float)(Size - 1);
            var c = new Color(.94f, .96f, 1f);
            c = Color.Lerp(c, new Color(.42f, .65f, .98f), Spot(u, v, .12f, .7f, .3f, .38f) * .8f);
            c = Color.Lerp(c, new Color(.67f, .51f, .95f), Spot(u, v, .87f, .64f, .35f, .42f) * .65f);
            c = Color.Lerp(c, new Color(1f, .68f, .76f), Spot(u, v, .68f, .08f, .4f, .33f) * .65f);
            // A fine curved ribbon makes the frosted refraction visible at the card edge.
            float ribbon = Mathf.Exp(-Mathf.Pow((v - .35f - .18f * Mathf.Sin(u * 5f)) / .016f, 2));
            pixels[y * Size + x] = Color.Lerp(c, Color.white, ribbon * .45f);
        }
        backdrop = Texture("Setup aurora", Size, Size, pixels);
        var blurred = Blur(pixels);
        for (int i = 0; i < blurred.Length; i++) blurred[i] = Color.Lerp(blurred[i], Color.white, .84f);
        frost = Texture("Setup frosted backdrop", Size, Size, blurred);
        var accent = new Color[64];
        for (int i = 0; i < accent.Length; i++) accent[i] = Color.Lerp(new Color(.02f, .29f, .67f), new Color(.32f, .18f, .64f), i / 63f);
        Accent = Texture("Setup accent", 64, 1, accent);
        Icon = MakeIcon();
        background = new Layer(root, backdrop, this) { name = "aurora-background" };
        glass = new Layer(root, frost, this) { name = "frosted-glass" };
        root.Insert(0, background); card.Insert(0, glass);
    }

    static float Spot(float x, float y, float cx, float cy, float sx, float sy) => Mathf.Exp(-((x - cx) * (x - cx) / (sx * sx) + (y - cy) * (y - cy) / (sy * sy)) * 2);

    static Texture2D Texture(string name, int width, int height, Color[] pixels)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixels(pixels); texture.Apply(false, true); return texture;
    }

    static Color[] Blur(Color[] input)
    {
        // Separable Gaussian, computed once. The glass samples this same backdrop in screen coordinates.
        const int radius = 12;
        var weights = new float[radius * 2 + 1]; float sum = 0;
        for (int k = -radius; k <= radius; k++) { float w = Mathf.Exp(-k * k / 50f); weights[k + radius] = w; sum += w; }
        var temporary = new Color[input.Length]; var output = new Color[input.Length];
        for (int pass = 0; pass < 2; pass++)
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            Color value = Color.clear;
            for (int k = -radius; k <= radius; k++)
            {
                int px = pass == 0 ? Mathf.Clamp(x + k, 0, Size - 1) : x;
                int py = pass == 0 ? y : Mathf.Clamp(y + k, 0, Size - 1);
                value += (pass == 0 ? input : temporary)[py * Size + px] * (weights[k + radius] / sum);
            }
            (pass == 0 ? temporary : output)[y * Size + x] = value;
        }
        return output;
    }

    public VisualElement CreateIcon()
    {
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("setup-icon"); icon.style.backgroundImage = new StyleBackground(Icon); return icon;
    }

    static Texture2D MakeIcon()
    {
        // Analytic edge coverage, at four times the nominal display size. Unlike raw triangles,
        // the one-display-pixel alpha edge remains smooth when the UI rotates or scales the icon.
        const int size = 416;
        var a = Spark(new Vector2(.43f, .43f), .36f);
        var b = Spark(new Vector2(.8f, .78f), .14f);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var p = new Vector2((x + .5f) / size, (y + .5f) / size);
            float distance = Mathf.Min(EdgeDistance(p, a), EdgeDistance(p, b));
            var color = Color.Lerp(new Color(.02f, .29f, .67f), new Color(.32f, .18f, .64f), Mathf.Clamp01((p.x + 1 - p.y) * .5f));
            color.a = Mathf.Clamp01(.5f - distance * 104); pixels[y * size + x] = color;
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = "Setup antialiased icon", wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear, hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixels(pixels); texture.Apply(true, true); return texture;
    }
    static Vector2[] Spark(Vector2 center, float radius)
    {
        var points = new Vector2[8];
        for (int i = 0; i < points.Length; i++)
        { float angle = i * Mathf.PI / 4; points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (i % 2 == 0 ? radius : radius * .32f); }
        return points;
    }
    static float EdgeDistance(Vector2 p, Vector2[] points)
    {
        bool inside = false; float distance = float.MaxValue;
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
            var a = points[j]; var b = points[i]; var edge = b - a;
            distance = Mathf.Min(distance, (p - a - edge * Mathf.Clamp01(Vector2.Dot(p - a, edge) / edge.sqrMagnitude)).magnitude);
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside ? -distance : distance;
    }

    public void Tick(float time, bool reduceMotion)
    {
        var next = reduceMotion ? Vector2.zero : new Vector2(Mathf.Sin(time * .16f) * .025f, Mathf.Sin(time * .12f) * .018f);
        if (reduceMotion && offset == next) return;
        if (!reduceMotion && time - lastFrame < 1f / 30) return;
        lastFrame = time; offset = next; background.MarkDirtyRepaint(); glass.MarkDirtyRepaint();
    }

    public void Dispose()
    { UnityEngine.Object.Destroy(backdrop); UnityEngine.Object.Destroy(frost); UnityEngine.Object.Destroy(Accent); UnityEngine.Object.Destroy(Icon); }

    sealed class Layer : VisualElement
    {
        readonly VisualElement screen;
        readonly Texture2D texture;
        readonly SetupAtmosphere owner;
        readonly Vertex[] vertices = new Vertex[4];
        static readonly ushort[] Indices = { 0, 1, 2, 2, 3, 0 };
        public Layer(VisualElement screen, Texture2D texture, SetupAtmosphere owner)
        {
            this.screen = screen; this.texture = texture; this.owner = owner;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute; style.left = 0; style.right = 0; style.top = 0; style.bottom = 0;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }
        void Draw(MeshGenerationContext context)
        {
            if (contentRect.width < 1 || contentRect.height < 1 || screen.worldBound.width < 1) return;
            var mesh = context.Allocate(4, 6, texture);
            // Newer UI Toolkit remaps atlas UVs itself; older Unity 6 versions require the region.
#if UNITY_6000_3_OR_NEWER
            Rect uv = new Rect(0, 0, 1, 1);
#else
            Rect uv = mesh.uvRegion;
#endif
            Rect bounds = worldBound, frame = screen.worldBound;
            for (int i = 0; i < 4; i++)
            {
                float x = i >= 2 ? contentRect.width : 0, y = i == 0 || i == 3 ? contentRect.height : 0;
                float u = .06f + .88f * (bounds.x + x - frame.x) / frame.width + owner.Offset.x;
                float v = .06f + .88f * (1 - (bounds.y + y - frame.y) / frame.height) + owner.Offset.y;
                vertices[i] = new Vertex { position = new Vector3(x, y, Vertex.nearZ), tint = Color.white, uv = new Vector2(uv.x + u * uv.width, uv.y + v * uv.height) };
            }
            mesh.SetAllVertices(vertices); mesh.SetAllIndices(Indices);
        }
    }
}
