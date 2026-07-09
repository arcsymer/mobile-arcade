// WHACK — an original one-thumb reflex game.
// Glowing targets pop up all over the screen; tap them before they shrink away. Miss three
// and you're out. Tap a red BOMB by mistake and it's instantly over. It speeds up as you go.
// Tap to start / retry.
//
// All procedural: sprites are runtime Texture2D, sound synthesised, HUD is IMGUI.
using System.Collections.Generic;
using UnityEngine;

public class Game : MonoBehaviour
{
    enum St { Menu, Play, Over }
    St state = St.Menu;

    Camera cam;
    Sprite circle, ring, xmark;
    float halfW, halfH;

    class Target { public Transform t; public SpriteRenderer sr; public float r, born, life; public bool bomb; }
    readonly List<Target> targets = new();

    class Part { public Transform t; public SpriteRenderer sr; public Vector2 v; public float life, max; }
    readonly List<Part> parts = new();

    int score, best, lives;
    float spawnT, spawnEvery, shake, flash;
    float elapsed;
    bool demo;
    AudioSource au;
    AudioClip sndHit, sndMiss, sndBomb;

    void Awake()
    {
        Application.targetFrameRate = 60;
        cam = Camera.main;
        if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
        cam.orthographic = true; cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.07f, 0.13f);
        cam.transform.position = new Vector3(0, 0, -10);

        circle = DiscSprite(96, false);
        ring = DiscSprite(96, true);
        xmark = XSprite(96);
        au = gameObject.AddComponent<AudioSource>();
        sndHit = Tone(720, 0.06f, 0.25f);
        sndMiss = Tone(180, 0.10f, 0.28f);
        sndBomb = Noise(0.4f, 0.45f);

        best = PlayerPrefs.GetInt("whack_best", 0);
        demo = Application.absoluteURL != null && Application.absoluteURL.ToLower().Contains("demo");
        Recompute();
    }

    public void EnableDemo() { demo = true; }
    void Recompute() { halfH = cam.orthographicSize; halfW = halfH * cam.aspect; }

    void ClearTargets() { foreach (var t in targets) Destroy(t.t.gameObject); targets.Clear(); }

    void Start_()
    {
        state = St.Play; score = 0; lives = 3; elapsed = 0; spawnT = 0; spawnEvery = 0.9f; overCd = 0;
        ClearTargets();
    }

    void Update()
    {
        Recompute();
        float dt = Time.deltaTime;
        bool tapped = Input.GetMouseButtonDown(0);

        if (state == St.Menu) { if (tapped || demo) Start_(); }
        else if (state == St.Play)
        {
            elapsed += dt;
            spawnEvery = Mathf.Max(0.4f, 0.9f - elapsed * 0.012f);
            spawnT += dt;
            if (spawnT >= spawnEvery) { spawnT = 0; Spawn(); }

            // expire targets; a good one expiring costs a life
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                var tg = targets[i];
                float age = elapsed - tg.born;
                float k = 1f - age / tg.life;
                if (k <= 0f)
                {
                    Vector3 at = tg.t.position; bool wasBomb = tg.bomb;
                    Destroy(tg.t.gameObject); targets.RemoveAt(i);
                    if (!wasBomb) LoseLife(at);
                    if (state != St.Play) return;   // LoseLife may have ended the game (ClearTargets emptied the list)
                    continue;
                }
                // pop-in then shrink-out scale for readability + urgency
                float appear = Mathf.Clamp01(age / 0.12f);
                float sc = tg.r * 2f * appear * (0.5f + 0.5f * k);
                tg.t.localScale = Vector3.one * sc;
            }

            if (tapped || demo) DoTap();
        }
        else { if ((tapped || demo) && overCd <= 0) Start_(); }

        if (overCd > 0) overCd -= dt;
        if (shake > 0) shake = Mathf.Max(0, shake - dt * 20f);
        if (flash > 0) flash = Mathf.Max(0, flash - dt * 2.5f);
        cam.transform.position = new Vector3(0, 0, -10) + (shake > 0 ? (Vector3)(Random.insideUnitCircle * shake * 0.05f) : Vector3.zero);
        UpdateParts(dt);
    }

    float overCd;

    void Spawn()
    {
        var go = new GameObject("tg");
        var sr = go.AddComponent<SpriteRenderer>();
        bool bomb = Random.value < 0.16f;
        float r = Random.Range(0.55f, 0.8f);
        sr.sprite = bomb ? xmark : circle; sr.sortingOrder = 2;
        sr.color = bomb ? new Color(1f, 0.35f, 0.35f) : Color.HSVToRGB(Random.Range(0.1f, 0.6f), 0.65f, 1f);
        float m = 0.7f;
        var pos = new Vector3(Random.Range(-halfW + m, halfW - m), Random.Range(-halfH + m + 0.6f, halfH - m - 0.6f), 0);
        go.transform.position = pos; go.transform.localScale = Vector3.zero;
        targets.Add(new Target { t = go.transform, sr = sr, r = r, born = elapsed, life = Mathf.Max(0.9f, 1.7f - elapsed * 0.02f), bomb = bomb });
    }

    void DoTap()
    {
        Vector3 w;
        if (demo) { var tg = PickDemoTarget(); if (tg == null) return; w = tg.t.position; }
        else w = cam.ScreenToWorldPoint(Input.mousePosition);
        // topmost (newest) target under the tap
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            var tg = targets[i];
            if (Vector2.Distance(w, tg.t.position) <= tg.r)
            {
                if (tg.bomb) { BombHit(tg.t.position); return; }
                score++; shake = 0.35f;
                Burst(tg.t.position, tg.sr.color, 10);
                Play(sndHit);
                Destroy(tg.t.gameObject); targets.RemoveAt(i);
                return;
            }
        }
    }

    Target PickDemoTarget()
    {
        // attract auto-pilot: tap the oldest good target (about to expire), never a bomb
        Target best = null; float bestAge = -1;
        foreach (var tg in targets)
        {
            if (tg.bomb) continue;
            float age = elapsed - tg.born;
            if (age > 0.15f && age > bestAge) { bestAge = age; best = tg; }
        }
        return best;
    }

    void LoseLife(Vector3 at)
    {
        lives--; flash = 1f; shake = 0.6f; Play(sndMiss);
        Burst(at, new Color(1f, 0.5f, 0.3f), 6);
        if (lives <= 0) GameOver();
    }

    void BombHit(Vector3 at)
    {
        shake = 1f; flash = 1f; Burst(at, new Color(1f, 0.4f, 0.4f), 24); Play(sndBomb);
        GameOver();
    }

    void GameOver()
    {
        state = St.Over; overCd = 0.4f;
        ClearTargets();
        if (score > best) { best = score; PlayerPrefs.SetInt("whack_best", best); PlayerPrefs.Save(); }
    }

    void Burst(Vector3 at, Color c, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("p"); var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = circle; sr.color = c; sr.sortingOrder = 3;
            float s = Random.Range(0.06f, 0.15f); go.transform.localScale = new Vector3(s, s, 1); go.transform.position = at;
            parts.Add(new Part { t = go.transform, sr = sr, v = Random.insideUnitCircle.normalized * Random.Range(1.5f, 5f), life = 0, max = Random.Range(0.35f, 0.6f) });
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
        var title = new GUIStyle(white) { fontSize = Mathf.RoundToInt(Mathf.Min(h * 0.085f, Screen.width * 0.15f)), fontStyle = FontStyle.Bold };
        var mid = new GUIStyle(white) { fontSize = Mathf.RoundToInt(h * 0.045f), fontStyle = FontStyle.Bold };
        var small = new GUIStyle(white) { fontSize = Mathf.RoundToInt(h * 0.032f) };

        if (flash > 0) { GUI.color = new Color(1f, 0.4f, 0.35f, flash * 0.35f); GUI.DrawTexture(new Rect(0, 0, Screen.width, h), Texture2D.whiteTexture); GUI.color = Color.white; }

        if (state == St.Play || state == St.Over)
        {
            GUI.Label(new Rect(0, h * 0.03f, Screen.width, h * 0.08f), score.ToString(), big);
            if (state == St.Play) GUI.Label(new Rect(0, h * 0.11f, Screen.width, h * 0.05f), "LIVES  " + Mathf.Max(0, lives), mid);
        }

        if (state == St.Menu)
        {
            GUI.Label(new Rect(0, h * 0.28f, Screen.width, h * 0.12f), "WHACK", title);
            GUI.Label(new Rect(0, h * 0.42f, Screen.width, h * 0.06f), "tap the dots • avoid the red ones", small);
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

    // ----- procedural sprites -----
    Sprite DiscSprite(int size, bool hollow)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float rad = size / 2f - 1f, c = size / 2f; var px = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c + 0.5f) * (x - c + 0.5f) + (y - c + 0.5f) * (y - c + 0.5f));
            float a = Mathf.Clamp01(rad - d);
            if (hollow) a *= Mathf.Clamp01(d - rad * 0.6f);
            px[y * size + x] = new Color(1, 1, 1, a);
        }
        t.SetPixels32(px); t.Apply(); t.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
    Sprite XSprite(int size)
    {
        // a filled disc with a white X cut-feel: disc + two diagonal bars drawn brighter
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float rad = size / 2f - 1f, c = size / 2f; var px = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float dx = x - c + 0.5f, dy = y - c + 0.5f; float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(rad - d);
            bool bar = Mathf.Abs(Mathf.Abs(dx) - Mathf.Abs(dy)) < size * 0.10f && d < rad * 0.72f;
            var col = bar ? new Color(1, 1, 1, a) : new Color(1, 1, 1, a); // color set on SR; bar just marks shape
            px[y * size + x] = col;
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
