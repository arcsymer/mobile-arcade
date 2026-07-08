// STAK — an original one-thumb tap-timing block stacker.
// A block slides side to side above the tower; tap anywhere to drop it. The part that
// overhangs the block below is sliced off (the tower gets narrower and harder); miss
// completely and it's game over. Stack as high as you can. Tap to start / retry.
//
// All procedural: sprites are runtime Texture2D, sound is synthesised, HUD is IMGUI.
using System.Collections.Generic;
using UnityEngine;

public class Game : MonoBehaviour
{
    enum St { Menu, Play, Over }
    St state = St.Menu;

    Camera cam;
    Sprite sq, circle;
    float halfW, halfH;

    class Block { public Transform t; public SpriteRenderer sr; public float w, x, y; }
    readonly List<Block> tower = new();
    Block mover;                 // the currently-sliding block
    float moveDir = 1f, moveSpeed = 3.2f;
    const float BlockH = 0.62f;
    float baseY;                 // world y of the tower's bottom
    float camY;                  // camera follows the top of the tower

    class Part { public Transform t; public SpriteRenderer sr; public Vector2 v; public float life, max; }
    readonly List<Part> parts = new();

    int score, best;
    float shake;
    bool demo;

    AudioSource au;
    AudioClip sndDrop, sndPerfect, sndOver;

    void Awake()
    {
        Application.targetFrameRate = 60;
        cam = Camera.main;
        if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
        cam.orthographic = true; cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.09f, 0.14f);

        sq = SquareSprite();
        circle = CircleSprite(48);
        au = gameObject.AddComponent<AudioSource>();
        sndDrop = Tone(300, 0.09f, 0.3f);
        sndPerfect = Tone(880, 0.12f, 0.3f);
        sndOver = Noise(0.35f, 0.4f);

        best = PlayerPrefs.GetInt("stak_best", 0);
        demo = Application.absoluteURL != null && Application.absoluteURL.ToLower().Contains("demo");
        Recompute();
        Reset();
    }

    public void EnableDemo() { demo = true; }
    void Recompute() { halfH = cam.orthographicSize; halfW = halfH * cam.aspect; }

    void Reset()
    {
        foreach (var b in tower) Destroy(b.t.gameObject);
        tower.Clear();
        if (mover != null) { Destroy(mover.t.gameObject); mover = null; }
        baseY = -halfH + 1.2f;
        camY = 0;
        cam.transform.position = new Vector3(0, camY, -10);
        // starting base block
        var b0 = MakeBlock(2.6f, 0f, baseY, Hue(0));
        tower.Add(b0);
        SpawnMover();
    }

    Block MakeBlock(float w, float x, float y, Color c)
    {
        var go = new GameObject("blk");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sq; sr.color = c;
        go.transform.position = new Vector3(x, y, 0);
        go.transform.localScale = new Vector3(w, BlockH, 1);
        return new Block { t = go.transform, sr = sr, w = w, x = x, y = y };
    }

    Color Hue(int i)
    {
        float h = (i * 0.06f) % 1f;
        return Color.HSVToRGB(h, 0.55f, 0.95f);
    }

    void SpawnMover()
    {
        var top = tower[tower.Count - 1];
        float y = top.y + BlockH;
        mover = MakeBlock(top.w, -halfW + top.w / 2f, y, Hue(tower.Count));
        moveDir = 1f;
        moveSpeed = 3.2f + tower.Count * 0.08f;
    }

    void Update()
    {
        Recompute();
        float dt = Time.deltaTime;

        bool tapped = Input.GetMouseButtonDown(0);

        if (state == St.Menu) { if (tapped || demo) Start_(); }
        else if (state == St.Play)
        {
            // slide the mover; bounce at the visible edges
            var p = mover.t.position;
            p.x += moveDir * moveSpeed * dt;
            float lim = halfW - mover.w / 2f;
            if (p.x > lim) { p.x = lim; moveDir = -1; }
            if (p.x < -lim) { p.x = -lim; moveDir = 1; }
            mover.t.position = p;
            mover.x = p.x;

            if (tapped || (demo && DemoShouldDrop())) Drop();
        }
        else { if ((tapped || demo) && overCd <= 0) Start_(); }

        if (overCd > 0) overCd -= dt;

        // camera eases toward the target height
        cam.transform.position = Vector3.Lerp(cam.transform.position, new Vector3(0, camY, -10), 1f - Mathf.Exp(-6f * dt))
                                 + (shake > 0 ? (Vector3)(Random.insideUnitCircle * shake * 0.05f) : Vector3.zero);
        if (shake > 0) shake = Mathf.Max(0, shake - dt * 20f);
        UpdateParts(dt);
    }

    float overCd;

    void Start_()
    {
        state = St.Play; score = 0; overCd = 0;
        Reset();
    }

    bool DemoShouldDrop()
    {
        // attract auto-pilot: drop when the mover roughly aligns with the block below
        var below = tower[tower.Count - 1];
        return Mathf.Abs(mover.x - below.x) < 0.12f;
    }

    void Drop()
    {
        var below = tower[tower.Count - 1];
        float left = Mathf.Max(mover.x - mover.w / 2f, below.x - below.w / 2f);
        float right = Mathf.Min(mover.x + mover.w / 2f, below.x + below.w / 2f);
        float overlap = right - left;

        if (overlap <= 0.02f) { GameOver(); return; }     // missed entirely

        float overhang = mover.w - overlap;
        // spawn a falling sliver for the sliced-off overhang (juice)
        if (overhang > 0.05f)
        {
            float sliverX = (mover.x > below.x) ? right + (mover.w - overlap) / 2f : left - (mover.w - overlap) / 2f;
            SliceSliver(sliverX, mover.t.position.y, overhang, mover.sr.color, mover.x > below.x);
        }

        // trim the mover to the overlapping region and lock it into the tower
        float newX = (left + right) / 2f;
        mover.w = overlap; mover.x = newX;
        mover.t.position = new Vector3(newX, mover.t.position.y, 0);
        mover.t.localScale = new Vector3(overlap, BlockH, 1);
        tower.Add(mover);

        bool perfect = overhang < 0.10f;
        score++;
        if (perfect) { shake = 0.5f; Play(sndPerfect); Burst(new Vector3(newX, mover.t.position.y, 0), mover.sr.color, 10); }
        else Play(sndDrop);

        camY = Mathf.Max(0, mover.t.position.y - (baseY + 2.5f));  // rise so the top stays framed
        mover = null;
        SpawnMover();
    }

    void GameOver()
    {
        state = St.Over; overCd = 0.4f; shake = 1f;
        Burst(mover.t.position, mover.sr.color, 20);
        Destroy(mover.t.gameObject); mover = null;
        if (score > best) { best = score; PlayerPrefs.SetInt("stak_best", best); PlayerPrefs.Save(); }
        Play(sndOver);
    }

    void SliceSliver(float x, float y, float w, Color c, bool goRight)
    {
        var go = new GameObject("sliver");
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = sq; sr.color = c;
        go.transform.position = new Vector3(x, y, 0);
        go.transform.localScale = new Vector3(w, BlockH, 1);
        parts.Add(new Part { t = go.transform, sr = sr, v = new Vector2(goRight ? 2.5f : -2.5f, 0.5f), life = 0, max = 1.1f });
    }

    void Burst(Vector3 at, Color c, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("p"); var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = circle; sr.color = c;
            float s = Random.Range(0.05f, 0.13f); go.transform.localScale = new Vector3(s, s, 1); go.transform.position = at;
            parts.Add(new Part { t = go.transform, sr = sr, v = Random.insideUnitCircle.normalized * Random.Range(1.5f, 5f), life = 0, max = Random.Range(0.4f, 0.7f) });
        }
    }

    void UpdateParts(float dt)
    {
        for (int i = parts.Count - 1; i >= 0; i--)
        {
            var p = parts[i]; p.life += dt;
            if (p.life >= p.max) { Destroy(p.t.gameObject); parts.RemoveAt(i); continue; }
            p.v += Vector2.down * 6f * dt;          // gravity
            p.t.position += (Vector3)(p.v * dt);
            var c = p.sr.color; c.a = 1f - p.life / p.max; p.sr.color = c;
        }
    }

    void OnGUI()
    {
        float h = Screen.height;
        var white = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = Color.white } };
        var big = new GUIStyle(white) { fontSize = Mathf.RoundToInt(h * 0.09f), fontStyle = FontStyle.Bold };
        var title = new GUIStyle(white) { fontSize = Mathf.RoundToInt(Mathf.Min(h * 0.085f, Screen.width * 0.16f)), fontStyle = FontStyle.Bold };
        var mid = new GUIStyle(white) { fontSize = Mathf.RoundToInt(h * 0.045f), fontStyle = FontStyle.Bold };
        var small = new GUIStyle(white) { fontSize = Mathf.RoundToInt(h * 0.032f) };

        if (state == St.Play || state == St.Over)
            GUI.Label(new Rect(0, h * 0.03f, Screen.width, h * 0.08f), score.ToString(), big);

        if (state == St.Menu)
        {
            GUI.Label(new Rect(0, h * 0.30f, Screen.width, h * 0.12f), "STAK", title);
            GUI.Label(new Rect(0, h * 0.44f, Screen.width, h * 0.06f), "tap to drop • line up the blocks", small);
            if (Time.unscaledTime % 1f < 0.6f) GUI.Label(new Rect(0, h * 0.60f, Screen.width, h * 0.07f), "tap to play", mid);
            GUI.Label(new Rect(0, h * 0.92f, Screen.width, h * 0.05f), "built by @arcsymer", small);
        }
        else if (state == St.Over)
        {
            GUI.Label(new Rect(0, h * 0.34f, Screen.width, h * 0.10f), "GAME OVER", title);
            GUI.Label(new Rect(0, h * 0.47f, Screen.width, h * 0.06f), "height  " + score, mid);
            GUI.Label(new Rect(0, h * 0.54f, Screen.width, h * 0.05f), "best  " + best, small);
            if (overCd <= 0 && Time.unscaledTime % 1f < 0.6f) GUI.Label(new Rect(0, h * 0.66f, Screen.width, h * 0.07f), "tap to retry", mid);
        }
    }

    // ----- procedural assets -----
    Sprite SquareSprite()
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; t.SetPixels32(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
    }
    Sprite CircleSprite(int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size / 2f - 1f, c = size / 2f; var px = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c + 0.5f) * (x - c + 0.5f) + (y - c + 0.5f) * (y - c + 0.5f));
            px[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(r - d));
        }
        t.SetPixels32(px); t.Apply(); t.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
    AudioClip Tone(float f, float dur, float vol)
    {
        int sr = 44100, n = Mathf.CeilToInt(sr * dur); var d = new float[n];
        for (int i = 0; i < n; i++) { float tt = (float)i / sr; d[i] = Mathf.Sin(2 * Mathf.PI * f * tt) * Mathf.Exp(-tt * 10f) * vol; }
        var c = AudioClip.Create("t", n, 1, sr, false); c.SetData(d, 0); return c;
    }
    AudioClip Noise(float dur, float vol)
    {
        int sr = 44100, n = Mathf.CeilToInt(sr * dur); var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = (Random.value * 2 - 1) * Mathf.Exp(-(float)i / n * 5f) * vol;
        var c = AudioClip.Create("n", n, 1, sr, false); c.SetData(d, 0); return c;
    }
    void Play(AudioClip c) { if (c) au.PlayOneShot(c); }
}
