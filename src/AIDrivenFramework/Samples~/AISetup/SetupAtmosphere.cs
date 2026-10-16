using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Small, original procedural textures. No camera, shader, downloaded art or per-frame blur.</summary>
sealed class SetupAtmosphere : IDisposable
{
    const int Size = 192;
    readonly Texture2D backdrop, frost;
    public readonly Texture2D Accent;
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

    public VisualElement CreateIcon() => new GradientIcon();

    sealed class GradientIcon : VisualElement
    {
        readonly Vertex[] vertices = new Vertex[18];
        readonly ushort[] indices = new ushort[48];
        public GradientIcon()
        {
            AddToClassList("setup-icon"); pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }
        void Draw(MeshGenerationContext context)
        {
            if (contentRect.width < 1) return;
            // Two four-point sparks, deliberately abstract rather than a character.
            Spark(0, new Vector2(.43f, .57f), .36f);
            Spark(1, new Vector2(.8f, .22f), .14f);
            var mesh = context.Allocate(vertices.Length, indices.Length);
            mesh.SetAllVertices(vertices); mesh.SetAllIndices(indices);
        }
        void Spark(int shape, Vector2 center, float radius)
        {
            int first = shape * 9;
            vertices[first] = Point(center);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4 - Mathf.PI / 2, length = i % 2 == 0 ? radius : radius * .32f;
                vertices[first + i + 1] = Point(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length);
                int index = shape * 24 + i * 3;
                indices[index] = (ushort)first; indices[index + 1] = (ushort)(first + i + 1); indices[index + 2] = (ushort)(first + (i + 1) % 8 + 1);
            }
        }
        Vertex Point(Vector2 p) => new Vertex
        {
            position = new Vector3(p.x * contentRect.width, p.y * contentRect.height, Vertex.nearZ),
            tint = Color.Lerp(new Color(.02f, .29f, .67f), new Color(.32f, .18f, .64f), Mathf.Clamp01((p.x + p.y) * .5f))
        };
    }

    public void Tick(float time, bool reduceMotion)
    {
        var next = reduceMotion ? Vector2.zero : new Vector2(Mathf.Sin(time * .16f) * .025f, Mathf.Sin(time * .12f) * .018f);
        if (reduceMotion && offset == next) return;
        if (!reduceMotion && time - lastFrame < 1f / 30) return;
        lastFrame = time; offset = next; background.MarkDirtyRepaint(); glass.MarkDirtyRepaint();
    }

    public void Dispose()
    { UnityEngine.Object.Destroy(backdrop); UnityEngine.Object.Destroy(frost); UnityEngine.Object.Destroy(Accent); }

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
