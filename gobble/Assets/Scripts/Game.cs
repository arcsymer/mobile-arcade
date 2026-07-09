// GOBBLE — an original one-thumb "eat & grow" time-killer.
// Drag your blob around. Eat blobs SMALLER than you to grow; touch a BIGGER blob and it's
// over. As you grow, blobs that used to threaten you become food. Tap to start / retry.
//
// All procedural: sprites are runtime Texture2D, sound synthesised, HUD is IMGUI.
using System.Collections.Generic;
using UnityEngine;

public class Game : MonoBehaviour
{
    enum St { Menu, Play, Over }
    St state = St.Menu;

    Camera cam;
    Sprite circle;
    float halfW, halfH;

    Transform player;
    SpriteRenderer playerSr;
    float pr;                         // player radius
    const float StartR = 0.42f, MaxR = 3.2f;
    Vector2 target;

    class Blob { public Transform t; public SpriteRenderer sr; public float r; public Vector2 v; }
    readonly List<Blob> blobs = new();
    const int COUNT = 16;

    class Part { public Transform t; public SpriteRenderer sr; public Vector2 v; public float life, max; }
    readonly List<Part> parts = new();

    int score, best;
    float shake;
    bool demo;
    AudioSource au;
    AudioClip sndEat, sndOver;

    void Awake()
    {
        Application.targetFrameRate = 60;
        cam = Camera.main;
        if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
        cam.orthographic = true; cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.12f, 0.13f);
        cam.transform.position = new Vector3(0, 0, -10);

        circle = CircleSprite(96);
        au = gameObject.AddComponent<AudioSource>();
        sndEat = Tone(520, 0.06f, 0.25f);
        sndOver = Noise(0.35f, 0.4f);

        best = PlayerPrefs.GetInt("gobble_best", 0);
        demo = Application.absoluteURL != null && Application.absoluteURL.ToLower().Contains("demo");

        var glow = MakeCircle(new Color(0.5f, 1f, 0.8f, 0.22f));
        player = MakeCircle(new Color(0.45f, 1f, 0.75f)); playerSr = player.GetComponent<SpriteRenderer>();
        glow.SetParent(player, false); glow.localScale = Vector3.one * 1.5f;

        Recompute();
        Reset();
    }

    public void EnableDemo() { demo = true; }
    void Recompute() { halfH = cam.orthographicSize; halfW = halfH * cam.aspect; }

    Transform MakeCircle(Color c)
    {
        var go = new GameObject("b"); var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = circle; sr.color = c; return go.transform;
    }

    void SetR(Transform t, float r) { t.localScale = Vector3.one * r * 2f; }

    void Reset()
    {
        pr = StartR; SetR(player, pr); player.position = Vector3.zero; target = Vector2.zero;
        foreach (var b in blobs) Destroy(b.t.gameObject);
        blobs.Clear();
        for (int i = 0; i < COUNT; i++) blobs.Add(SpawnBlob(true));
        score = 0;
    }

    Blob SpawnBlob(bool anywhere)
    {
        var t = MakeCircle(Color.white);
        var b = new Blob { t = t, sr = t.GetComponent<SpriteRenderer>() };
        Respawn(b, anywhere);
        return b;
    }

    void Respawn(Blob b, bool anywhere)
    {
        // sizes span both smaller (food) and a bit bigger (threats) than the player
        float r = Random.value < 0.66f ? pr * Random.Range(0.35f, 0.92f) : pr * Random.Range(1.08f, 1.7f);
        r = Mathf.Clamp(r, 0.18f, MaxR * 1.4f);
        b.r = r; SetR(b.t, r);
        float hue = (r < pr) ? Random.Range(0.5f, 0.62f) : Random.Range(0.95f, 1.02f) % 1f; // food teal-ish, threats red-ish
        b.sr.color = Color.HSVToRGB(Mathf.Repeat(hue, 1f), 0.55f, 0.95f);
        // spawn at a random edge so they drift in
        Vector2 p;
        if (anywhere) p = new Vector2(Random.Range(-halfW, halfW), Random.Range(-halfH, halfH));
        else { int e = Random.Range(0, 4); p = e == 0 ? new Vector2(-halfW - r, Random.Range(-halfH, halfH)) : e == 1 ? new Vector2(halfW + r, Random.Range(-halfH, halfH)) : e == 2 ? new Vector2(Random.Range(-halfW, halfW), -halfH - r) : new Vector2(Random.Range(-halfW, halfW), halfH + r); }
        // don't spawn a threat right on top of the player
        if (Vector2.Distance(p, player.position) < pr + r + 0.5f) p += (p - (Vector2)player.position).normalized * 2f;
        b.t.position = p;
        b.v = Random.insideUnitCircle.normalized * Random.Range(0.4f, 1.3f);
    }

    void Update()
    {
        Recompute();
        float dt = Time.deltaTime;
        bool down = Input.GetMouseButton(0);
        bool tapped = Input.GetMouseButtonDown(0);

        if (state == St.Menu) { DriftBlobs(dt); if (tapped || demo) Start_(); }
        else if (state == St.Play)
        {
            if (demo) target = DemoTarget();
            else if (down) { Vector3 w = cam.ScreenToWorldPoint(Input.mousePosition); target = w; }
            // ease the player toward the target, clamped to the arena
            Vector2 pp = Vector2.Lerp(player.position, target, 1f - Mathf.Exp(-10f * dt));
            pp.x = Mathf.Clamp(pp.x, -halfW + pr, halfW - pr);
            pp.y = Mathf.Clamp(pp.y, -halfH + pr, halfH - pr);
            player.position = pp;

            DriftBlobs(dt);

            for (int i = 0; i < blobs.Count; i++)
            {
                var b = blobs[i];
                float d = Vector2.Distance(b.t.position, player.position);
                if (d < pr + b.r * 0.4f)  // meaningful overlap
                {
                    if (b.r < pr * 0.94f) { Eat(b); }
                    else { GameOver(); return; }
                }
            }
        }
        else { DriftBlobs(dt); if ((tapped || demo) && overCd <= 0) Start_(); }

        if (overCd > 0) overCd -= dt;
        cam.transform.position = new Vector3(0, 0, -10) + (shake > 0 ? (Vector3)(Random.insideUnitCircle * shake * 0.05f) : Vector3.zero);
        if (shake > 0) shake = Mathf.Max(0, shake - dt * 20f);
        UpdateParts(dt);
    }

    float overCd;

    void DriftBlobs(float dt)
    {
        for (int i = 0; i < blobs.Count; i++)
        {
            var b = blobs[i]; var p = (Vector2)b.t.position + b.v * dt;
            if (p.x < -halfW - b.r || p.x > halfW + b.r || p.y < -halfH - b.r || p.y > halfH + b.r)
            { if (state == St.Play) Respawn(b, false); else { b.v = -b.v; p = b.t.position; } }
            b.t.position = p;
        }
    }

    void Start_() { state = St.Play; overCd = 0; Reset(); }

    void Eat(Blob b)
    {
        pr = Mathf.Min(MaxR, Mathf.Sqrt(pr * pr + b.r * b.r * 0.35f)); // area-based growth, capped
        SetR(player, pr);
        score += Mathf.Max(1, Mathf.RoundToInt(b.r * 10f));
        Burst(b.t.position, b.sr.color, 8);
        Play(sndEat);
        Respawn(b, false);
    }

    void GameOver()
    {
        state = St.Over; overCd = 0.4f; shake = 1f;
        Burst(player.position, playerSr.color, 22);
        if (score > best) { best = score; PlayerPrefs.SetInt("gobble_best", best); PlayerPrefs.Save(); }
        Play(sndOver);
    }

    Vector2 DemoTarget()
    {
        // head to the nearest edible blob; flee if a bigger one is very close
        float bestD = 999f; Vector2 goal = Vector2.zero; bool found = false;
        Vector2 flee = Vector2.zero; bool threat = false;
        foreach (var b in blobs)
        {
            float d = Vector2.Distance(b.t.position, player.position);
            if (b.r >= pr * 0.94f) { if (d < pr + b.r + 1.2f) { flee += ((Vector2)player.position - (Vector2)b.t.position).normalized / Mathf.Max(0.3f, d); threat = true; } }
            else if (d < bestD) { bestD = d; goal = b.t.position; found = true; }
        }
        if (threat) return (Vector2)player.position + flee.normalized * 3f;
        return found ? goal : Vector2.zero;
    }

    void Burst(Vector3 at, Color c, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("p"); var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = circle; sr.color = c;
            float s = Random.Range(0.05f, 0.14f); go.transform.localScale = new Vector3(s, s, 1); go.transform.position = at;
            parts.Add(new Part { t = go.transform, sr = sr, v = Random.insideUnitCircle.normalized * Random.Range(1.5f, 5f), life = 0, max = Random.Range(0.35f, 0.65f) });
        }
    }
    void UpdateParts(float dt)
    {
        for (int i = parts.Count - 1; i >= 0; i--)
        {
            var p = parts[i]; p.life += dt;
            if (p.life >= p.max) { Destroy(p.t.gameObject); parts.RemoveAt(i); continue; }
            p.v *= (1f - 3f * dt); p.t.position += (Vector3)(p.v * dt);
            var c = p.sr.color; c.a = 1f - p.life / p.max; p.sr.color = c;
        }
    }

    void OnGUI()
    {
        float h = Screen.height;
        var white = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = Color.white } };
        var big = new GUIStyle(white) { fontSize = Mathf.RoundToInt(h * 0.09f), fontStyle = FontStyle.Bold };
        var title = new GUIStyle(white) { fontSize = Mathf.RoundToInt(Mathf.Min(h * 0.085f, Screen.width * 0.14f)), fontStyle = FontStyle.Bold };
        var mid = new GUIStyle(white) { fontSize = Mathf.RoundToInt(h * 0.045f), fontStyle = FontStyle.Bold };
        var small = new GUIStyle(white) { fontSize = Mathf.RoundToInt(h * 0.032f) };

        if (state == St.Play || state == St.Over)
            GUI.Label(new Rect(0, h * 0.03f, Screen.width, h * 0.08f), score.ToString(), big);

        if (state == St.Menu)
        {
            GUI.Label(new Rect(0, h * 0.28f, Screen.width, h * 0.12f), "GOBBLE", title);
            GUI.Label(new Rect(0, h * 0.42f, Screen.width, h * 0.06f), "eat smaller • dodge bigger • grow", small);
            if (Time.unscaledTime % 1f < 0.6f) GUI.Label(new Rect(0, h * 0.58f, Screen.width, h * 0.07f), "tap to play", mid);
            GUI.Label(new Rect(0, h * 0.92f, Screen.width, h * 0.05f), "built by @arcsymer", small);
        }
        else if (state == St.Over)
        {
            GUI.Label(new Rect(0, h * 0.34f, Screen.width, h * 0.10f), "GAME OVER", title);
            GUI.Label(new Rect(0, h * 0.47f, Screen.width, h * 0.06f), "score  " + score, mid);
            GUI.Label(new Rect(0, h * 0.54f, Screen.width, h * 0.05f), "best  " + best, small);
            if (overCd <= 0 && Time.unscaledTime % 1f < 0.6f) GUI.Label(new Rect(0, h * 0.66f, Screen.width, h * 0.07f), "tap to retry", mid);
        }
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
        for (int i = 0; i < n; i++) { float tt = (float)i / sr; d[i] = Mathf.Sin(2 * Mathf.PI * f * tt) * Mathf.Exp(-tt * 11f) * vol; }
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
