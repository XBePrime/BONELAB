using System;
using System.Collections.Generic;
using BoneLib;
using Il2CppSLZ.Marrow;
using LabFusion.Player;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BePrime.Ghost;

/// <summary>
/// Cyberpunk-style yellow hologram floating on the LEFT FOREARM.
/// Poke with right index fingertip.
/// </summary>
public static class GhostHolo
{
    private enum Tab { Nick, Lobby, Custom }

    // Landscape holo plate (~19cm x ~7cm)
    private const float CanvasW = 480f;
    private const float CanvasH = 168f;
    private const float WorldScale = 0.00040f;

    private static GameObject _root;
    private static RectTransform _canvasRt;
    private static RectTransform _panel;
    private static RectTransform _content;
    private static RectTransform _toastRt;
    private static Image _toastBg;
    private static Image _scanSweep;
    private static Text _toastTitle;
    private static Text _toastBody;
    private static Text _headerSub;
    private static Font _font;

    private static readonly List<HoloBtn> _buttons = new List<HoloBtn>();
    private static Tab _tab = Tab.Nick;
    private static string _keypad = "";
    private static int _playerPage;
    private static bool _rebuildQueued;
    private static Tab _queuedTab;

    private static int _hoverIndex = -1;
    private static int _insideIndex = -1;
    private static float _clickLockUntil;
    private static float _prevBestPlane = 99f;
    private const float ClickCooldown = 0.42f;
    private const float PlaneMax = 0.028f;
    private const float PlaneEnter = 0.018f;
    private const float EdgePad = 0.12f;

    private static float _toastT = 1f;
    private static float _toastHoldUntil;
    private static string _toastTitleStr = "";
    private static string _toastBodyStr = "";
    private const float ToastIn = 0.12f;
    private const float ToastHold = 1.7f;
    private const float ToastOut = 0.2f;

    private static float _appearT = 1f;
    private static float _baseScale = WorldScale;
    private static float _scanT;

    // CP2077-ish yellow holo glass
    private static readonly Color Glass = new Color(0.18f, 0.14f, 0.02f, 0.42f);
    private static readonly Color GlassDeep = new Color(0.10f, 0.08f, 0.01f, 0.55f);
    private static readonly Color Frame = new Color(1f, 0.90f, 0.12f, 0.55f);
    private static readonly Color Yellow = new Color(1f, 0.91f, 0.14f, 0.95f);
    private static readonly Color YellowSoft = new Color(1f, 0.86f, 0.20f, 0.55f);
    private static readonly Color YellowDim = new Color(0.70f, 0.55f, 0.08f, 0.35f);
    private static readonly Color YellowHot = new Color(1f, 0.96f, 0.55f, 0.85f);
    private static readonly Color RowIdle = new Color(1f, 0.88f, 0.15f, 0.10f);
    private static readonly Color RowHover = new Color(1f, 0.90f, 0.20f, 0.28f);
    private static readonly Color RowActive = new Color(1f, 0.85f, 0.10f, 0.38f);
    private static readonly Color TextCol = new Color(1f, 0.94f, 0.55f, 0.92f);
    private static readonly Color TextDim = new Color(0.85f, 0.72f, 0.25f, 0.70f);
    private static readonly Color Danger = new Color(1f, 0.32f, 0.18f, 0.55f);
    private static readonly Color DangerText = new Color(1f, 0.55f, 0.40f, 0.95f);
    private static readonly Color ToastBg = new Color(0.12f, 0.10f, 0.02f, 0.72f);
    private static readonly Color Scan = new Color(1f, 0.92f, 0.20f, 0.07f);

    private sealed class HoloBtn
    {
        public RectTransform Rt;
        public Image Bg;
        public Image Accent;
        public Text Label;
        public Action OnClick;
        public bool DangerStyle;
        public bool AccentStyle;
        public float PressAnim;
        public float HoverBlend;
        public Vector3 BaseScale;
    }

    public static void Tick()
    {
        if (!GhostMod.Enabled)
        {
            Destroy();
            return;
        }
        if (!GhostMod.FusionLoaded) return;

        Ensure();
        if (_root == null) return;

        if (_rebuildQueued)
        {
            _rebuildQueued = false;
            Rebuild(_queuedTab);
        }

        float dt = Time.unscaledDeltaTime;
        AttachToLeftForearm();
        AnimateAppear(dt);
        AnimateScan(dt);
        AnimateButtons(dt);
        AnimateToast(dt);
        HandleTouch();
    }

    public static void Notify(string title, string body)
    {
        _toastTitleStr = string.IsNullOrEmpty(title) ? "GHOST" : title.ToUpperInvariant();
        _toastBodyStr = body ?? "";
        _toastT = 0f;
        _toastHoldUntil = Time.unscaledTime + ToastHold;
        ApplyToastVisual();
    }

    public static void ShowToast(string msg) => Notify("GHOST", msg);

    public static void Destroy()
    {
        _buttons.Clear();
        _hoverIndex = -1;
        _insideIndex = -1;
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
            _canvasRt = null;
            _panel = null;
            _content = null;
            _toastRt = null;
            _toastBg = null;
            _scanSweep = null;
            _toastTitle = null;
            _toastBody = null;
            _headerSub = null;
        }
        _appearT = 1f;
    }

    private static void Ensure()
    {
        if (_root != null) return;
        try
        {
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _root = new GameObject("BE_PRIME_GHOST_HOLO");
            Object.DontDestroyOnLoad(_root);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 90;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 16f;
            _root.AddComponent<GraphicRaycaster>();

            _canvasRt = _root.GetComponent<RectTransform>();
            _canvasRt.sizeDelta = new Vector2(CanvasW, CanvasH);
            _baseScale = WorldScale;
            _root.transform.localScale = Vector3.one * _baseScale;

            // Soft outer glow frame
            var glow = MakeImage(_root.transform, "Glow", new Color(1f, 0.85f, 0.1f, 0.12f));
            Stretch(glow.rectTransform);
            glow.rectTransform.offsetMin = new Vector2(-6f, -6f);
            glow.rectTransform.offsetMax = new Vector2(6f, 6f);

            // Thin neon border
            var border = MakeImage(_root.transform, "Border", Frame);
            Stretch(border.rectTransform);

            // Glass plate
            _panel = MakeImage(_root.transform, "Glass", Glass).rectTransform;
            Stretch(_panel);
            Inset(_panel, 2f);

            // Inner wash
            var wash = MakeImage(_panel, "Wash", GlassDeep);
            Stretch(wash.rectTransform);
            Inset(wash.rectTransform, 1f);

            // Scanlines (static bands)
            BuildScanlines(_panel);

            // Moving sweep
            _scanSweep = MakeImage(_panel, "Sweep", Scan);
            SetAnchors(_scanSweep.rectTransform, 0f, 0f, 1f, 0f);
            _scanSweep.rectTransform.pivot = new Vector2(0.5f, 0f);
            _scanSweep.rectTransform.sizeDelta = new Vector2(0f, 18f);
            _scanSweep.rectTransform.anchoredPosition = Vector2.zero;

            // Corner brackets (CP chrome)
            AddCorner(_panel, "TL", true, true);
            AddCorner(_panel, "TR", false, true);
            AddCorner(_panel, "BL", true, false);
            AddCorner(_panel, "BR", false, false);

            BuildHeader(_panel);
            BuildTabsBar(_panel);

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(_panel, false);
            _content = contentGo.AddComponent<RectTransform>();
            SetAnchors(_content, 0f, 0f, 1f, 1f);
            _content.offsetMin = new Vector2(10f, 10f);
            _content.offsetMax = new Vector2(-10f, -52f);

            BuildToast();
            _appearT = 0f;
            Rebuild(_tab);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Ghost holo build: {ex}");
            Destroy();
        }
    }

    private static void BuildHeader(RectTransform parent)
    {
        var top = MakeImage(parent, "Header", new Color(1f, 0.85f, 0.1f, 0.08f)).rectTransform;
        SetAnchors(top, 0f, 1f, 1f, 1f);
        top.pivot = new Vector2(0.5f, 1f);
        top.sizeDelta = new Vector2(0f, 28f);
        top.anchoredPosition = Vector2.zero;
        InsetX(top, 8f);

        var mark = MakeImage(top, "Mark", Yellow).rectTransform;
        SetAnchors(mark, 0f, 0.2f, 0f, 0.8f);
        mark.pivot = new Vector2(0f, 0.5f);
        mark.sizeDelta = new Vector2(3f, 0f);
        mark.anchoredPosition = new Vector2(6f, 0f);

        var title = MakeText(top, "Title", "GHOST  //  NETRUNNER", 13, Yellow, TextAnchor.MiddleLeft);
        SetAnchors(title.rectTransform, 0f, 0f, 0.62f, 1f);
        title.rectTransform.offsetMin = new Vector2(14f, 0f);
        title.rectTransform.offsetMax = new Vector2(0f, 0f);
        title.fontStyle = FontStyle.Bold;

        _headerSub = MakeText(top, "Sub", "LINK ACTIVE", 10, TextDim, TextAnchor.MiddleRight);
        SetAnchors(_headerSub.rectTransform, 0.55f, 0f, 1f, 1f);
        _headerSub.rectTransform.offsetMin = new Vector2(0f, 0f);
        _headerSub.rectTransform.offsetMax = new Vector2(-10f, 0f);

        var line = MakeImage(parent, "HeaderLine", YellowSoft).rectTransform;
        SetAnchors(line, 0f, 1f, 1f, 1f);
        line.pivot = new Vector2(0.5f, 1f);
        line.sizeDelta = new Vector2(0f, 1.2f);
        line.anchoredPosition = new Vector2(0f, -28f);
        InsetX(line, 10f);
    }

    private static void BuildTabsBar(RectTransform parent)
    {
        var tabs = MakeImage(parent, "Tabs", new Color(1f, 0.85f, 0.1f, 0.06f)).rectTransform;
        SetAnchors(tabs, 0f, 1f, 1f, 1f);
        tabs.pivot = new Vector2(0.5f, 1f);
        tabs.sizeDelta = new Vector2(0f, 22f);
        tabs.anchoredPosition = new Vector2(0f, -30f);
        InsetX(tabs, 8f);
    }

    private static void BuildScanlines(RectTransform parent)
    {
        var host = new GameObject("Scanlines");
        host.transform.SetParent(parent, false);
        var rt = host.AddComponent<RectTransform>();
        Stretch(rt);
        for (int i = 0; i < 14; i++)
        {
            float y = 1f - (i + 0.5f) / 14f;
            var line = MakeImage(rt, "SL" + i, new Color(1f, 0.9f, 0.2f, i % 2 == 0 ? 0.035f : 0.018f));
            SetAnchors(line.rectTransform, 0f, y, 1f, y);
            line.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            line.rectTransform.sizeDelta = new Vector2(0f, 2.2f);
            line.rectTransform.anchoredPosition = Vector2.zero;
        }
    }

    private static void AddCorner(RectTransform parent, string name, bool left, bool top)
    {
        float ax = left ? 0f : 1f;
        float ay = top ? 1f : 0f;
        var go = new GameObject("Corner_" + name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(ax, ay);
        rt.anchorMax = new Vector2(ax, ay);
        rt.pivot = new Vector2(ax, ay);
        rt.sizeDelta = new Vector2(18f, 18f);
        rt.anchoredPosition = new Vector2(left ? 4f : -4f, top ? -4f : 4f);

        var h = MakeImage(rt, "H", Yellow).rectTransform;
        h.anchorMin = new Vector2(left ? 0f : 0.35f, top ? 0.85f : 0f);
        h.anchorMax = new Vector2(left ? 0.65f : 1f, top ? 1f : 0.15f);
        h.offsetMin = Vector2.zero;
        h.offsetMax = Vector2.zero;

        var v = MakeImage(rt, "V", Yellow).rectTransform;
        v.anchorMin = new Vector2(left ? 0f : 0.85f, top ? 0.35f : 0f);
        v.anchorMax = new Vector2(left ? 0.15f : 1f, top ? 1f : 0.65f);
        v.offsetMin = Vector2.zero;
        v.offsetMax = Vector2.zero;
    }

    private static void BuildToast()
    {
        var go = new GameObject("Toast");
        go.transform.SetParent(_panel, false);
        _toastRt = go.AddComponent<RectTransform>();
        SetAnchors(_toastRt, 0f, 1f, 1f, 1f);
        _toastRt.pivot = new Vector2(0.5f, 1f);
        _toastRt.sizeDelta = new Vector2(0f, 36f);
        _toastRt.anchoredPosition = new Vector2(0f, 42f);
        InsetX(_toastRt, 12f);

        _toastBg = go.AddComponent<Image>();
        _toastBg.color = ToastBg;

        var accent = MakeImage(_toastRt, "Accent", Yellow).rectTransform;
        SetAnchors(accent, 0f, 0f, 0f, 1f);
        accent.pivot = new Vector2(0f, 0.5f);
        accent.sizeDelta = new Vector2(3f, 0f);

        _toastTitle = MakeText(_toastRt, "TTitle", "", 10, Yellow, TextAnchor.MiddleLeft);
        SetAnchors(_toastTitle.rectTransform, 0f, 0.48f, 1f, 1f);
        _toastTitle.rectTransform.offsetMin = new Vector2(10f, 0f);
        _toastTitle.rectTransform.offsetMax = new Vector2(-6f, -2f);

        _toastBody = MakeText(_toastRt, "TBody", "", 11, TextCol, TextAnchor.MiddleLeft);
        SetAnchors(_toastBody.rectTransform, 0f, 0f, 1f, 0.55f);
        _toastBody.rectTransform.offsetMin = new Vector2(10f, 2f);
        _toastBody.rectTransform.offsetMax = new Vector2(-6f, 0f);

        _toastT = 1f;
        ApplyToastVisual();
    }

    /// <summary>
    /// Locked to forearm bones only — no palm.up, no head tracking (those made it roll away).
    /// Plate flat on inner forearm, face toward the player.
    /// </summary>
    private static void AttachToLeftForearm()
    {
        try
        {
            if (_root == null) return;

            RigManager rm = Player.RigManager;
            ArtRig art = rm?.physicsRig?.artOutput;
            Transform lower = art?.artLowerArmLf;
            Transform wrist = art?.artWristLf;
            Hand hand = Player.LeftHand;

            if (lower == null || wrist == null)
            {
                if (hand == null) return;
                Transform palm = hand.palmPositionTransform != null ? hand.palmPositionTransform : hand.transform;
                Vector3 pos = palm.TransformPoint(new Vector3(0f, 0.05f, -0.12f));
                // Face player: UI front is -forward → point forward into the palm
                Quaternion rot = Quaternion.LookRotation(-palm.up, -palm.forward);
                rot *= Quaternion.Euler(0f, 0f, -90f);
                _root.transform.SetPositionAndRotation(pos, rot);
                return;
            }

            Vector3 along = wrist.position - lower.position;
            if (along.sqrMagnitude < 1e-8f) return;
            along.Normalize();

            // Stable "out of inner forearm" from the BONE axes (follows arm, not fingers).
            Vector3 a = Vector3.ProjectOnPlane(lower.up, along);
            Vector3 b = Vector3.ProjectOnPlane(lower.right, along);
            Vector3 c = Vector3.ProjectOnPlane(lower.forward, along);
            Vector3 outward = a;
            if (b.sqrMagnitude > outward.sqrMagnitude) outward = b;
            if (c.sqrMagnitude > outward.sqrMagnitude) outward = c;
            if (outward.sqrMagnitude < 1e-8f) return;
            outward.Normalize();

            // Flip once toward the palm side (sign only — does not track finger curl)
            if (hand?.palmPositionTransform != null)
            {
                Vector3 toPalm = hand.palmPositionTransform.position - lower.position;
                if (Vector3.Dot(outward, toPalm) < 0f)
                    outward = -outward;
            }

            // Mid-forearm, lifted off the skin toward the player
            Vector3 posMid = Vector3.Lerp(lower.position, wrist.position, 0.40f);
            posMid += outward * 0.07f;

            // Unity world canvas is readable from the -forward side.
            // Point forward INTO the arm so the FACE looks at the player.
            // up = toward elbow → long side of the plate runs across the arm (horizontal).
            Quaternion rot = Quaternion.LookRotation(-outward, -along);
            // 90° clockwise (вправо), as requested from the start
            rot *= Quaternion.Euler(0f, 0f, -90f);

            _root.transform.SetPositionAndRotation(posMid, rot);
        }
        catch { /* rig missing */ }
    }

    private static void AnimateAppear(float dt)
    {
        if (_appearT >= 1f || _root == null) return;
        _appearT = Mathf.Min(1f, _appearT + dt / 0.18f);
        float e = EaseOutCubic(_appearT);
        float s = _baseScale * Mathf.Lerp(0.88f, 1f, e);
        _root.transform.localScale = Vector3.one * s;
    }

    private static void AnimateScan(float dt)
    {
        if (_scanSweep == null) return;
        _scanT += dt * 0.35f;
        if (_scanT > 1f) _scanT -= 1f;
        float y = Mathf.Lerp(0f, CanvasH - 28f, _scanT);
        _scanSweep.rectTransform.anchoredPosition = new Vector2(0f, y);
        Color c = Scan;
        c.a = 0.04f + 0.05f * Mathf.Sin(_scanT * Mathf.PI);
        _scanSweep.color = c;
    }

    private static void AnimateButtons(float dt)
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            HoloBtn b = _buttons[i];
            if (b?.Rt == null || b.Bg == null) continue;

            bool hover = i == _hoverIndex;
            b.HoverBlend = Mathf.MoveTowards(b.HoverBlend, hover ? 1f : 0f, dt * 12f);
            if (b.PressAnim > 0f)
                b.PressAnim = Mathf.Max(0f, b.PressAnim - dt / 0.14f);

            float punch = b.PressAnim > 0f
                ? 1f - Mathf.Sin((1f - b.PressAnim) * Mathf.PI) * 0.06f
                : 1f;
            b.Rt.localScale = b.BaseScale * punch;

            Color idle = b.DangerStyle ? new Color(Danger.r, Danger.g, Danger.b, 0.18f)
                : (b.AccentStyle ? RowActive : RowIdle);
            Color hot = b.DangerStyle ? Danger : RowHover;
            b.Bg.color = Color.Lerp(idle, hot, b.HoverBlend);

            if (b.Accent != null)
            {
                Color a = b.DangerStyle ? DangerText : Yellow;
                a.a = Mathf.Lerp(0.35f, 0.95f, b.HoverBlend);
                if (b.AccentStyle) a.a = 0.95f;
                b.Accent.color = a;
            }

            if (b.Label != null)
            {
                Color tc = b.DangerStyle ? DangerText : TextCol;
                if (b.AccentStyle) tc = Yellow;
                tc.a = Mathf.Lerp(0.75f, 1f, b.HoverBlend);
                b.Label.color = tc;
            }
        }
    }

    private static void AnimateToast(float dt)
    {
        if (_toastRt == null) return;
        if (_toastT < 1f)
        {
            if (Time.unscaledTime < _toastHoldUntil)
                _toastT = Mathf.MoveTowards(_toastT, 0f, dt / ToastIn);
            else
                _toastT = Mathf.MoveTowards(_toastT, 1f, dt / ToastOut);
        }
        ApplyToastVisual();
    }

    private static void ApplyToastVisual()
    {
        if (_toastRt == null) return;
        float e = EaseOutCubic(1f - _toastT);
        _toastRt.anchoredPosition = new Vector2(0f, Mathf.Lerp(40f, -2f, e));
        if (_toastBg != null)
        {
            Color c = ToastBg; c.a = ToastBg.a * e; _toastBg.color = c;
        }
        if (_toastTitle != null)
        {
            _toastTitle.text = "// " + _toastTitleStr;
            var c = Yellow; c.a = e; _toastTitle.color = c;
        }
        if (_toastBody != null)
        {
            _toastBody.text = _toastBodyStr;
            var c = TextCol; c.a = e; _toastBody.color = c;
        }
    }

    private static void HandleTouch()
    {
        if (!TryGetRightIndexTip(out Vector3 tip))
        {
            _prevBestPlane = 99f;
            return;
        }

        if (_buttons.Count == 0) return;

        int best = -1;
        float bestAbs = float.MaxValue;
        for (int i = 0; i < _buttons.Count; i++)
        {
            HoloBtn b = _buttons[i];
            if (b?.Rt == null) continue;
            if (!TryHitRect(tip, b.Rt, EdgePad, out float plane, out bool inside))
                continue;
            if (!inside) continue;
            float a = Mathf.Abs(plane);
            if (a > PlaneMax) continue;
            if (a < bestAbs)
            {
                bestAbs = a;
                best = i;
            }
        }

        _hoverIndex = best;

        // Poke = tip crosses into contact depth while still over the button
        bool poke = best >= 0 && bestAbs <= PlaneEnter && _prevBestPlane > PlaneEnter;
        _prevBestPlane = best >= 0 ? bestAbs : 99f;

        if (poke && Time.unscaledTime >= _clickLockUntil)
        {
            _insideIndex = best;
            FireButton(best);
        }
        else if (best < 0)
        {
            _insideIndex = -1;
        }
    }

    private static bool TryHitRect(Vector3 tip, RectTransform rt, float edgePad, out float planeDist, out bool inside)
    {
        planeDist = 99f;
        inside = false;
        if (rt == null) return false;

        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector3 bl = corners[0], tl = corners[1], tr = corners[2];
        Vector3 right = tr - tl;
        Vector3 up = tl - bl;
        Vector3 normal = Vector3.Cross(right, up);
        if (normal.sqrMagnitude < 1e-10f) return false;
        normal.Normalize();

        Vector3 center = (bl + tr) * 0.5f;
        planeDist = Vector3.Dot(tip - center, normal);

        float w = right.magnitude;
        float h = up.magnitude;
        if (w < 1e-5f || h < 1e-5f) return false;

        Vector3 rN = right / w;
        Vector3 uN = up / h;
        Vector3 projected = tip - normal * planeDist;
        Vector3 local = projected - bl;
        float u = Vector3.Dot(local, rN);
        float v = Vector3.Dot(local, uN);

        float padW = w * edgePad;
        float padH = h * edgePad;
        inside = u >= -padW && u <= w + padW && v >= -padH && v <= h + padH;
        return true;
    }

    private static void FireButton(int index)
    {
        if (index < 0 || index >= _buttons.Count) return;
        HoloBtn b = _buttons[index];
        if (b == null) return;
        _clickLockUntil = Time.unscaledTime + ClickCooldown;
        b.PressAnim = 1f;
        try { b.OnClick?.Invoke(); }
        catch (Exception ex) { MelonLogger.Warning($"Ghost btn: {ex.Message}"); }
    }

    private static bool TryGetRightIndexTip(out Vector3 tip)
    {
        tip = default;
        try
        {
            RigManager rm = Player.RigManager;
            if (rm?.physicsRig?.artOutput?.artFingerRt13 != null)
            {
                tip = rm.physicsRig.artOutput.artFingerRt13.position;
                return true;
            }
            Hand hand = Player.RightHand;
            if (hand != null)
            {
                if (hand.palmPositionTransform != null)
                {
                    tip = hand.palmPositionTransform.TransformPoint(new Vector3(0.01f, 0.02f, 0.06f));
                    return true;
                }
                tip = hand.transform.TransformPoint(new Vector3(0f, 0.02f, 0.05f));
                return true;
            }
        }
        catch { }
        return false;
    }

    private static void QueueRebuild(Tab tab)
    {
        _queuedTab = tab;
        _rebuildQueued = true;
    }

    private static void Rebuild(Tab tab)
    {
        _tab = tab;
        ClearContent();
        if (_panel == null || _content == null) return;

        AddTab(0, "IDENTITY", Tab.Nick);
        AddTab(1, "LOBBY", Tab.Lobby);
        AddTab(2, "MOD.IO", Tab.Custom);

        switch (tab)
        {
            case Tab.Nick: BuildNick(); break;
            case Tab.Lobby: BuildLobby(); break;
            case Tab.Custom: BuildCustom(); break;
        }

        if (_headerSub != null)
            _headerSub.text = tab == Tab.Lobby && !GhostLobby.IsHost ? "HOST ONLY" : "LINK ACTIVE";

        _insideIndex = -1;
        _hoverIndex = -1;
    }

    private static void BuildNick()
    {
        // Left column — identity ops
        var left = MakeSection(_content, "ID", "IDENTITY", 0f, 0f, 0.48f, 1f);
        float y = -4f;
        AddRow(left, ref y, "HIDE NAME", "braille blank", () =>
        {
            GhostIdentity.ApplyInvisible();
            Notify("NICK", "Name hidden");
        });
        AddRow(left, ref y, "SET · GHOST", "preset", () =>
        {
            GhostIdentity.ApplyPreset("GHOST");
            Notify("NICK", "Set to GHOST");
        });
        AddRow(left, ref y, "SET · UNKNOWN", "preset", () =>
        {
            GhostIdentity.ApplyPreset("UNKNOWN");
            Notify("NICK", "Set to UNKNOWN");
        });
        AddRow(left, ref y, "SET · ANON", "preset", () =>
        {
            GhostIdentity.ApplyPreset("ANON");
            Notify("NICK", "Set to ANON");
        });
        AddRow(left, ref y, "RESTORE PROFILE", "revert", () =>
        {
            GhostIdentity.Restore();
            Notify("RESTORE", "Real profile back");
        }, danger: true);

        // Right column — session players
        var right = MakeSection(_content, "PLY", "SESSION PLAYERS", 0.52f, 0f, 1f, 1f);
        float ry = -4f;
        var players = GhostIdentity.ListSessionPlayers();
        if (players.Count == 0)
        {
            AddHint(right, ref ry, "No other players in session");
        }
        else
        {
            int start = _playerPage * 4;
            int shown = 0;
            for (int i = start; i < players.Count && shown < 4; i++, shown++)
            {
                var entry = players[i];
                PlayerID id = entry.id;
                string captured = entry.label;
                string name = Trim(entry.label, 14);
                AddRow(right, ref ry, "CLONE · " + name, "copy identity", () =>
                {
                    GhostIdentity.CloneFromPlayer(id);
                    Notify("CLONE", Trim(captured, 16));
                });
            }
            if (players.Count > 4)
            {
                AddRow(right, ref ry, "NEXT PAGE ›", $"{_playerPage + 1}/{Mathf.CeilToInt(players.Count / 4f)}", () =>
                {
                    _playerPage++;
                    if (_playerPage * 4 >= players.Count) _playerPage = 0;
                    QueueRebuild(Tab.Nick);
                }, accent: true);
            }
        }
    }

    private static void BuildLobby()
    {
        var left = MakeSection(_content, "LB", "LOBBY SPOOF", 0f, 0f, 0.48f, 1f);
        float y = -4f;
        string hostHint = GhostLobby.IsHost ? $"fakes online · {GhostLobby.Fakes.Count}" : "host required";
        AddHint(left, ref y, hostHint);
        AddRow(left, ref y, "ADD FAKE SLOT", "inject playerinfo", () =>
        {
            int before = GhostLobby.Fakes.Count;
            GhostLobby.AddFake();
            if (GhostLobby.Fakes.Count > before)
            {
                Notify("LOBBY", $"Fake +1 · {GhostLobby.Fakes.Count}");
                QueueRebuild(Tab.Lobby);
            }
        }, accent: true);
        AddRow(left, ref y, "CLEAR ALL FAKES", "wipe metadata", () =>
        {
            if (!GhostLobby.IsHost)
            {
                Notify("DENIED", GhostLobby.EnsureHostOrError());
                return;
            }
            GhostLobby.ClearFakes();
            Notify("LOBBY", "Fakes cleared");
            QueueRebuild(Tab.Lobby);
        }, danger: true);

        var right = MakeSection(_content, "AV", "AVATAR ICON", 0.52f, 0f, 1f, 1f);
        float ry = -4f;
        AddHint(right, ref ry, "apply local avatar meta");
        for (int i = 0; i < GhostLobby.AvatarPresets.Length && i < 5; i++)
        {
            string av = GhostLobby.AvatarPresets[i];
            AddRow(right, ref ry, av.ToUpperInvariant(), "icon preset", () =>
            {
                GhostIdentity.ApplyAvatarMeta(av, -1);
                Notify("AVATAR", "Icon → " + av);
            });
        }
    }

    private static void BuildCustom()
    {
        var left = MakeSection(_content, "IN", "MOD.IO ID", 0f, 0f, 0.42f, 1f);
        float y = -6f;
        AddHint(left, ref y, "enter numeric mod id");

        var display = MakeImage(left, "Display", new Color(1f, 0.88f, 0.12f, 0.12f)).rectTransform;
        display.anchorMin = new Vector2(0f, 1f);
        display.anchorMax = new Vector2(1f, 1f);
        display.pivot = new Vector2(0.5f, 1f);
        display.sizeDelta = new Vector2(0f, 28f);
        display.anchoredPosition = new Vector2(0f, y);
        var edge = MakeImage(display, "E", YellowSoft);
        Stretch(edge.rectTransform);
        Inset(edge.rectTransform, 0f);
        edge.color = new Color(Yellow.r, Yellow.g, Yellow.b, 0.35f);
        var fill = MakeImage(display, "F", RowIdle);
        Stretch(fill.rectTransform);
        Inset(fill.rectTransform, 1f);
        string shown = string.IsNullOrEmpty(_keypad) ? "——" : _keypad + "_";
        var dispTxt = MakeText(display, "V", shown, 16, Yellow, TextAnchor.MiddleCenter);
        Stretch(dispTxt.rectTransform);

        var right = MakeSection(_content, "KP", "KEYPAD", 0.46f, 0f, 1f, 1f);
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "CLR", "0", "OK" };
        for (int i = 0; i < keys.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            string key = keys[i];
            float x = 4f + col * 58f;
            float yy = -4f - row * 26f;
            bool danger = key == "CLR";
            bool accent = key == "OK";
            AddRowAt(right, x, yy, 54f, 22f, key, null, () =>
            {
                if (key == "CLR")
                {
                    _keypad = "";
                    Notify("CODE", "Cleared");
                }
                else if (key == "OK")
                {
                    if (int.TryParse(_keypad, out int modId) && modId > 0)
                    {
                        GhostIdentity.ApplyAvatarMeta("mod.io/" + modId, modId);
                        Notify("MOD.IO", "Linked " + modId);
                    }
                    else Notify("ERROR", "Need numeric id");
                }
                else if (_keypad.Length < 9)
                {
                    _keypad += key;
                }
                QueueRebuild(Tab.Custom);
            }, danger, accent);
        }
    }

    private static RectTransform MakeSection(Transform parent, string id, string title, float x0, float y0, float x1, float y1)
    {
        var go = new GameObject("Sec_" + id);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        SetAnchors(rt, x0, y0, x1, y1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var bg = MakeImage(rt, "Bg", new Color(1f, 0.88f, 0.12f, 0.05f));
        Stretch(bg.rectTransform);

        var border = MakeImage(rt, "Bd", new Color(1f, 0.9f, 0.15f, 0.22f));
        Stretch(border.rectTransform);
        // hollow feel: shrink fill slightly via nested wash
        var inner = MakeImage(rt, "In", new Color(0.05f, 0.04f, 0.01f, 0.15f));
        Stretch(inner.rectTransform);
        Inset(inner.rectTransform, 1f);

        var head = MakeText(rt, "H", title, 9, TextDim, TextAnchor.MiddleLeft);
        SetAnchors(head.rectTransform, 0f, 1f, 1f, 1f);
        head.rectTransform.pivot = new Vector2(0f, 1f);
        head.rectTransform.sizeDelta = new Vector2(0f, 14f);
        head.rectTransform.anchoredPosition = Vector2.zero;
        head.rectTransform.offsetMin = new Vector2(6f, -14f);
        head.rectTransform.offsetMax = new Vector2(-4f, 0f);

        var body = new GameObject("Body");
        body.transform.SetParent(rt, false);
        var brt = body.AddComponent<RectTransform>();
        SetAnchors(brt, 0f, 0f, 1f, 1f);
        brt.offsetMin = new Vector2(5f, 4f);
        brt.offsetMax = new Vector2(-5f, -16f);
        return brt;
    }

    private static void AddHint(Transform host, ref float y, string text)
    {
        var t = MakeText(host, "Hint", text, 9, TextDim, TextAnchor.MiddleLeft);
        t.rectTransform.anchorMin = new Vector2(0f, 1f);
        t.rectTransform.anchorMax = new Vector2(1f, 1f);
        t.rectTransform.pivot = new Vector2(0f, 1f);
        t.rectTransform.sizeDelta = new Vector2(0f, 12f);
        t.rectTransform.anchoredPosition = new Vector2(2f, y);
        y -= 14f;
    }

    private static void AddTab(int index, string label, Tab tab)
    {
        var tabs = _panel.Find("Tabs");
        if (tabs == null) return;
        float w = 78f;
        float x = 6f + index * (w + 6f);
        bool active = _tab == tab;
        AddRowAt(tabs, x, -2f, w, 18f, label, null, () => QueueRebuild(tab), false, active);
    }

    private static void AddRow(Transform host, ref float y, string label, string sub, Action act, bool danger = false, bool accent = false)
    {
        AddRowAt(host, 0f, y, -1f, 20f, label, sub, act, danger, accent);
        y -= 22f;
    }

    private static void AddRowAt(Transform host, float x, float y, float w, float h, string label, string sub, Action act, bool danger = false, bool accent = false)
    {
        var go = new GameObject("Row_" + label);
        go.transform.SetParent(host, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = w < 0f ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(w < 0f ? 0f : w, h);
        rt.anchoredPosition = new Vector2(x, y);

        var fillImg = go.AddComponent<Image>();
        fillImg.color = accent ? RowActive : RowIdle;
        fillImg.raycastTarget = false;

        var accentBar = MakeImage(rt, "Acc", Yellow).rectTransform;
        SetAnchors(accentBar, 0f, 0.15f, 0f, 0.85f);
        accentBar.pivot = new Vector2(0f, 0.5f);
        accentBar.sizeDelta = new Vector2(2f, 0f);
        accentBar.anchoredPosition = new Vector2(2f, 0f);
        var accentImg = accentBar.GetComponent<Image>();

        var txt = MakeText(rt, "L", label, 10, TextCol, TextAnchor.MiddleLeft);
        SetAnchors(txt.rectTransform, 0f, 0f, 1f, 1f);
        txt.rectTransform.offsetMin = new Vector2(8f, 0f);
        txt.rectTransform.offsetMax = new Vector2(sub != null ? -48f : -4f, 0f);
        if (accent) { txt.color = Yellow; txt.fontStyle = FontStyle.Bold; }
        if (danger) txt.color = DangerText;

        if (!string.IsNullOrEmpty(sub))
        {
            var st = MakeText(rt, "S", sub, 8, TextDim, TextAnchor.MiddleRight);
            SetAnchors(st.rectTransform, 0.45f, 0f, 1f, 1f);
            st.rectTransform.offsetMin = new Vector2(0f, 0f);
            st.rectTransform.offsetMax = new Vector2(-4f, 0f);
        }

        _buttons.Add(new HoloBtn
        {
            Rt = rt,
            Bg = fillImg,
            Accent = accentImg,
            Label = txt,
            OnClick = act,
            DangerStyle = danger,
            AccentStyle = accent,
            BaseScale = Vector3.one
        });
    }

    private static void ClearContent()
    {
        for (int i = 0; i < _buttons.Count; i++)
            if (_buttons[i]?.Rt != null) Object.Destroy(_buttons[i].Rt.gameObject);
        _buttons.Clear();

        if (_panel == null) return;
        var tabs = _panel.Find("Tabs");
        if (tabs != null)
            for (int i = tabs.childCount - 1; i >= 0; i--)
                Object.Destroy(tabs.GetChild(i).gameObject);

        if (_content != null)
            for (int i = _content.childCount - 1; i >= 0; i--)
                Object.Destroy(_content.GetChild(i).gameObject);
    }

    private static string Trim(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    }

    private static float EaseOutCubic(float x)
    {
        float t = 1f - x;
        return 1f - t * t * t;
    }

    private static Image MakeImage(Transform parent, string name, Color col)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = col;
        img.raycastTarget = false;
        return img;
    }

    private static Text MakeText(Transform parent, string name, string value, int size, Color col, TextAnchor anchor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var t = go.AddComponent<Text>();
        t.font = _font;
        t.text = value;
        t.fontSize = size;
        t.color = col;
        t.alignment = anchor;
        t.raycastTarget = false;
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetAnchors(RectTransform rt, float x0, float y0, float x1, float y1)
    {
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
    }

    private static void Inset(RectTransform rt, float v)
    {
        rt.offsetMin = new Vector2(v, v);
        rt.offsetMax = new Vector2(-v, -v);
    }

    private static void InsetX(RectTransform rt, float v)
    {
        rt.offsetMin = new Vector2(v, rt.offsetMin.y);
        rt.offsetMax = new Vector2(-v, rt.offsetMax.y);
    }
}
