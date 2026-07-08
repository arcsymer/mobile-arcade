// STARDODGE — an original one-thumb endless-dodge time-killer.
// Steer a glowing craft left/right (drag / hold anywhere) to weave through falling
// meteors. Survive; the longer you last the faster they come. Tap to start / restart.
//
// Everything is procedural: sprites are generated as Texture2D at runtime, sound is
// synthesised into AudioClips, and the whole scene is built in code — no imported art.
using System.Collections.Generic;
using UnityEngine;

public class Game : MonoBehaviour
{
    enum St { Menu, Play, Over }
    St state = St.Menu;

    Camera cam;
    Sprite circle, square, tri;
    Transform player;
    SpriteRenderer playerSr;

    class Meteor { public Transform t; public float r, vy, spin, spinV; }
    readonly List<Meteor> meteors = new();
    readonly Stack<Meteor> pool = new();

    class Part { public Transform t; public Vector2 v; public float life, max; public SpriteRenderer sr; }
    readonly List<Part> parts = new();

    Transform[] stars;
    float[] starSpeed;

    float score, best, timeAlive, spawnT, spawnEvery = 1.1f, shake, flash;
    float halfW, halfH, targetX;
    const float PlayerR = 0.34f, PlayerY = -3.7f;
    Color cPlayer = new Color(0.45f, 0.9f, 1f), cMeteor = new Color(1f, 0.55f, 0.3f);

    AudioSource audioSrc;
    AudioClip sndTick, sndCrash, sndStart;
    int lastTickScore;
    bool demo; // attract-mode auto-pilot (toggle with the D key) — auto-plays + dodges + loops

    void Awake()
    {
        Application.targetFrameRate = 60;
        cam = Camera.main;
        if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.04f, 0.10f);
        cam.transform.position = new Vector3(0, 0, -10);

        circle = CircleSprite(72);
        square = SquareSprite();
        tri = TriangleSprite(72);

        audioSrc = gameObject.AddComponent<AudioSource>();
        sndTick = Tone(660, 0.05f, 0.25f);
        sndStart = Tone(520, 0.12f, 0.3f);
        sndCrash = Noise(0.35f, 0.4f);

        // starfield
        stars = new Transform[46];
        starSpeed = new float[stars.Length];
        for (int i = 0; i < stars.Length; i++)
        {
            var s = Quad(square, new Color(0.75f, 0.82f, 1f, Random.Range(0.15f, 0.6f)), 5);
            float sc = Random.Range(0.03f, 0.09f);
            s.localScale = new Vector3(sc, sc, 1);
            stars[i] = s;
            starSpeed[i] = Random.Range(0.5f, 1.8f);
        }

        // player craft (triangle with a soft glow behind it)
        var glow = Quad(circle, new Color(cPlayer.r, cPlayer.g, cPlayer.b, 0.28f), 1);
        glow.localScale = Vector3.one * PlayerR * 3.4f;
        player = Quad(tri, cPlayer, 2);
        player.localScale = Vector3.one * PlayerR * 2.4f;
        glow.SetParent(player, false);
        playerSr = player.GetComponent<SpriteRenderer>();

        best = PlayerPrefs.GetFloat("stardodge_best", 0f);
        // attract-mode auto-pilot can be forced via ?demo=1 in the URL (used for capture)
        demo = Application.absoluteURL != null && Application.absoluteURL.ToLower().Contains("demo");
        Recompute();
        PlaceStars(true);
        ResetPlayer();
    }

    // called from JS via unityInstance.SendMessage("Game","EnableDemo") — reliable attract trigger
    public void EnableDemo() { demo = true; }

    void Recompute() { halfH = cam.orthographicSize; halfW = halfH * cam.aspect; }
    void ResetPlayer() { player.position = new Vector3(0, PlayerY, 0); targetX = 0; }

    void Update()
    {
        Recompute();
        float dt = Time.deltaTime;

        // starfield drifts down always (menu + play), wraps at bottom
        for (int i = 0; i < stars.Length; i++)
        {
            var p = stars[i].position;
            p.y -= starSpeed[i] * dt * (state == St.Play ? 1.6f : 1f);
            if (p.y < -halfH - 0.2f) { p.y = halfH + 0.2f; p.x = Random.Range(-halfW, halfW); }
            stars[i].position = p;
        }

        if (Input.GetKeyDown(KeyCode.D)) demo = !demo; // toggle attract-mode auto-pilot

        bool down = Input.GetMouseButton(0);
        bool tapped = Input.GetMouseButtonDown(0);

        if (state == St.Menu)
        {
            if (tapped || demo) StartGame();
        }
        else if (state == St.Play)
        {
            // steer toward the pointer's x while held; in demo mode the auto-pilot dodges
            if (demo)
            {
                targetX = DemoTargetX();
            }
            else if (down)
            {
                Vector3 w = cam.ScreenToWorldPoint(Input.mousePosition);
                targetX = Mathf.Clamp(w.x, -halfW + PlayerR, halfW - PlayerR);
            }
            var pp = player.position;
            pp.x = Mathf.Lerp(pp.x, targetX, 1f - Mathf.Exp(-14f * dt));
            player.position = pp;

            timeAlive += dt;
            score += dt * 10f;
            if ((int)score / 50 > lastTickScore) { lastTickScore = (int)score / 50; Play(sndTick); }

            // difficulty ramps: faster + more frequent over time
            spawnEvery = Mathf.Max(0.32f, 1.1f - timeAlive * 0.02f);
            spawnT += dt;
            if (spawnT >= spawnEvery) { spawnT = 0; SpawnMeteor(); }

            UpdateMeteors(dt, true);
        }
        else // Over
        {
            UpdateMeteors(dt, false); // keep them falling behind the overlay
            if ((demo || Input.GetMouseButtonDown(0)) && overCooldown <= 0) StartGame();
        }

        if (overCooldown > 0) overCooldown -= dt;
        UpdateParts(dt);

        // screen shake + hit flash decay
        if (shake > 0) shake = Mathf.Max(0, shake - dt * 22f);
        Vector3 baseCam = new Vector3(0, 0, -10);
        cam.transform.position = baseCam + (shake > 0 ? (Vector3)(Random.insideUnitCircle * shake * 0.06f) : Vector3.zero);
        if (flash > 0) flash = Mathf.Max(0, flash - dt * 2.5f);
    }

    float overCooldown;

    void StartGame()
    {
        state = St.Play;
        score = 0; timeAlive = 0; spawnT = 0; spawnEvery = 1.1f; lastTickScore = 0;
        ClearMeteors();
        ResetPlayer();
        playerSr.enabled = true;
        Play(sndStart);
    }

    void GameOver()
    {
        state = St.Over;
        overCooldown = 0.5f; // ignore the same tap that killed you
        shake = 1f; flash = 1f;
        Burst(player.position, cPlayer, 26);
        playerSr.enabled = false;
        if (score > best) { best = score; PlayerPrefs.SetFloat("stardodge_best", best); PlayerPrefs.Save(); }
        Play(sndCrash);
    }

    // ----- meteors ---------------------------------------------------------------
    void SpawnMeteor()
    {
        var m = pool.Count > 0 ? pool.Pop() : NewMeteor();
        m.r = Random.Range(0.28f, 0.62f);
        m.t.localScale = Vector3.one * m.r * 2f;
        m.t.position = new Vector3(Random.Range(-halfW + m.r, halfW - m.r), halfH + m.r + 0.3f, 0);
        m.vy = Random.Range(2.6f, 3.6f) + timeAlive * 0.09f;
        m.spin = Random.Range(0f, 360f);
        m.spinV = Random.Range(-90f, 90f);
        m.t.gameObject.SetActive(true);
        meteors.Add(m);
    }
    Meteor NewMeteor()
    {
        var t = Quad(circle, cMeteor, 3);
        return new Meteor { t = t };
    }
    void UpdateMeteors(float dt, bool checkHit)
    {
        for (int i = meteors.Count - 1; i >= 0; i--)
        {
            var m = meteors[i];
            var p = m.t.position;
            p.y -= m.vy * dt;
            m.t.position = p;
            m.spin += m.spinV * dt;
            m.t.rotation = Quaternion.Euler(0, 0, m.spin);
            if (checkHit)
            {
                float dx = p.x - player.position.x, dy = p.y - player.position.y;
                float rr = m.r + PlayerR * 0.82f;
                if (dx * dx + dy * dy < rr * rr) { GameOver(); }
            }
            if (p.y < -halfH - m.r - 0.4f) { Recycle(m); meteors.RemoveAt(i); }
        }
    }
    void Recycle(Meteor m) { m.t.gameObject.SetActive(false); pool.Push(m); }
    void ClearMeteors() { for (int i = 0; i < meteors.Count; i++) Recycle(meteors[i]); meteors.Clear(); }

    // attract-mode auto-pilot: steer away from the most threatening incoming meteor
    float DemoTargetX()
    {
        float px = player.position.x, py = player.position.y;
        float threatX = 0f, best = -999f; bool found = false;
        for (int i = 0; i < meteors.Count; i++)
        {
            var m = meteors[i];
            float relY = m.t.position.y - py;
            if (relY < 0f || relY > 4.5f) continue;                    // above the player and close
            float ax = Mathf.Abs(m.t.position.x - px);
            if (ax > m.r + PlayerR + 1.3f) continue;                   // roughly in our lane
            float threat = (4.5f - relY) - ax;                         // nearer + more aligned = bigger threat
            if (threat > best) { best = threat; threatX = m.t.position.x; found = true; }
        }
        if (!found) return Mathf.Lerp(px, 0f, 0.6f);                    // clear skies: ease to centre
        float dir = (threatX >= px) ? -1f : 1f;                        // move away from the threat
        float t = px + dir * 2.4f;
        if (t < -halfW + PlayerR || t > halfW - PlayerR) t = px - dir * 2.4f; // avoid the wall
        return Mathf.Clamp(t, -halfW + PlayerR, halfW - PlayerR);
    }

    // ----- particles -------------------------------------------------------------
    void Burst(Vector3 at, Color c, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var t = Quad(circle, c, 4);
            float sc = Random.Range(0.06f, 0.16f);
            t.localScale = new Vector3(sc, sc, 1);
            t.position = at;
            parts.Add(new Part { t = t, v = Random.insideUnitCircle.normalized * Random.Range(2f, 7f), life = 0, max = Random.Range(0.4f, 0.8f), sr = t.GetComponent<SpriteRenderer>() });
        }
    }
    void UpdateParts(float dt)
    {
        for (int i = parts.Count - 1; i >= 0; i--)
        {
            var p = parts[i];
            p.life += dt;
            if (p.life >= p.max) { Destroy(p.t.gameObject); parts.RemoveAt(i); continue; }
            p.v *= (1f - 2.5f * dt);
            p.t.position += (Vector3)(p.v * dt);
            var c = p.sr.color; c.a = 1f - p.life / p.max; p.sr.color = c;
        }
    }

    void PlaceStars(bool spread)
    {
        for (int i = 0; i < stars.Length; i++)
            stars[i].position = new Vector3(Random.Range(-halfW, halfW), spread ? Random.Range(-halfH, halfH) : halfH + 0.2f, 0);
    }

    // ----- HUD (IMGUI — no scene UI / fonts needed) ------------------------------
    void OnGUI()
    {
        float h = Screen.height;
        GUI.depth = 0;
        var white = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = Color.white } };
        var big = new GUIStyle(white) { fontSize = Mathf.RoundToInt(h * 0.09f), fontStyle = FontStyle.Bold };
        var title = new GUIStyle(white) { fontSize = Mathf.RoundToInt(Mathf.Min(h * 0.085f, Screen.width * 0.13f)), fontStyle = FontStyle.Bold };
        var mid = new GUIStyle(white) { fontSize = Mathf.RoundToInt(h * 0.045f), fontStyle = FontStyle.Bold };
        var small = new GUIStyle(white) { fontSize = Mathf.RoundToInt(h * 0.032f) };

        if (flash > 0) { var t = Tex(new Color(1f, 0.5f, 0.35f, flash * 0.4f)); GUI.DrawTexture(new Rect(0, 0, Screen.width, h), t); }

        if (state == St.Play || state == St.Over)
            GUI.Label(new Rect(0, h * 0.03f, Screen.width, h * 0.08f), ((int)score).ToString(), big);

        if (state == St.Menu)
        {
            GUI.Label(new Rect(0, h * 0.30f, Screen.width, h * 0.12f), "STARDODGE", title);
            GUI.Label(new Rect(0, h * 0.44f, Screen.width, h * 0.06f), "weave through the meteors", small);
            if (Time.unscaledTime % 1f < 0.6f)
                GUI.Label(new Rect(0, h * 0.60f, Screen.width, h * 0.07f), "tap to play", mid);
            GUI.Label(new Rect(0, h * 0.92f, Screen.width, h * 0.05f), "built by @arcsymer", small);
        }
        else if (state == St.Over)
        {
            GUI.Label(new Rect(0, h * 0.34f, Screen.width, h * 0.10f), "GAME OVER", title);
            GUI.Label(new Rect(0, h * 0.47f, Screen.width, h * 0.06f), "score  " + (int)score, mid);
            GUI.Label(new Rect(0, h * 0.54f, Screen.width, h * 0.05f), "best  " + (int)best, small);
            if (overCooldown <= 0 && Time.unscaledTime % 1f < 0.6f)
                GUI.Label(new Rect(0, h * 0.66f, Screen.width, h * 0.07f), "tap to retry", mid);
        }
    }

    // ----- procedural assets -----------------------------------------------------
    Transform Quad(Sprite s, Color c, int order)
    {
        var go = new GameObject("q");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s; sr.color = c; sr.sortingOrder = order;
        return go.transform;
    }

    static Texture2D _t1;
    Texture Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

    Sprite SquareSprite()
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; t.SetPixels32(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
    }
    Sprite CircleSprite(int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size / 2f - 1f, c = size / 2f;
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c + 0.5f) * (x - c + 0.5f) + (y - c + 0.5f) * (y - c + 0.5f));
                float a = Mathf.Clamp01(r - d);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
        t.SetPixels32(px); t.Apply(); t.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
    Sprite TriangleSprite(int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fy = (float)y / size;                 // 0 bottom .. 1 top
                float halfWidth = fy * 0.5f;                 // widens toward the top (points up)
                float cx = (float)x / size - 0.5f;
                float a = (Mathf.Abs(cx) < halfWidth && fy > 0.05f) ? 1f : 0f;
                px[y * size + x] = new Color(1, 1, 1, a);
            }
        t.SetPixels32(px); t.Apply(); t.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    AudioClip Tone(float freq, float dur, float vol)
    {
        int sr = 44100, n = Mathf.CeilToInt(sr * dur);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float tt = (float)i / sr;
            float env = Mathf.Exp(-tt * 12f);
            data[i] = Mathf.Sin(2 * Mathf.PI * freq * tt) * env * vol;
        }
        var clip = AudioClip.Create("tone", n, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }
    AudioClip Noise(float dur, float vol)
    {
        int sr = 44100, n = Mathf.CeilToInt(sr * dur);
        var data = new float[n];
        for (int i = 0; i < n; i++) { float env = Mathf.Exp(-(float)i / n * 5f); data[i] = (Random.value * 2 - 1) * env * vol; }
        var clip = AudioClip.Create("noise", n, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }
    void Play(AudioClip c) { if (c != null) audioSrc.PlayOneShot(c); }
}
