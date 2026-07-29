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
/// Cyberpunk wrist hologram on the left hand.
/// Input: poke buttons with the right index fingertip (no A-button).
/// Short one-shot press / toast animations only — no looping FX.
/// </summary>
public static class GhostHolo
{
    private enum Tab { Nick, Lobby, Custom }

    private static GameObject _root;
    private static RectTransform _panel;
    private static RectTransform _toastRt;
    private static Image _toastBg;
    private static Text _toastTitle;
    private static Text _toastBody;
    private static Text _body;
    private static Text _headerSub;
    private static readonly List<HoloBtn> _buttons = new List<HoloBtn>();
    private static Tab _tab = Tab.Nick;
    private static string _keypad = "";
    private static string _status = "SYSTEM READY";
    private static int _playerPage;
    private static Font _font;

    private static bool _rebuildQueued;
    private static Tab _queuedTab;

    // Touch
    private static int _hoverIndex = -1;
    private static int _insideIndex = -1;
    private static float _clickLockUntil;
    private const float ClickCooldown = 0.38f;
    private const float TouchEnter = 0.038f;
    private const float TouchExit = 0.048f; // hysteresis

    // Toast anim
    private static float _toastT = 1f; // 0 = showing, 1 = hidden
    private static float _toastHoldUntil;
    private static string _toastTitleStr = "";
    private static string _toastBodyStr = "";
    private const float ToastIn = 0.14f;
    private const float ToastHold = 1.85f;
    private const float ToastOut = 0.22f;

    // Appear
    private static float _appearT = 1f;

    // Cyberpunk palette
    private static readonly Color Bg = new Color(0.015f, 0.04f, 0.055f, 0.94f);
    private static readonly Color Panel = new Color(0.03f, 0.09f, 0.12f, 0.96f);
    private static readonly Color Cyan = new Color(0.05f, 0.92f, 1f, 1f);
    private static readonly Color CyanDim = new Color(0.04f, 0.42f, 0.52f, 1f);
    private static readonly Color CyanHot = new Color(0.35f, 1f, 1f, 1f);
    private static readonly Color Orange = new Color(1f, 0.42f, 0.05f, 1f);
    private static readonly Color OrangeDim = new Color(0.72f, 0.26f, 0.04f, 1f);
    private static readonly Color TextCol = new Color(0.82f, 0.96f, 1f, 1f);
    private static readonly Color Danger = new Color(1f, 0.22f, 0.28f, 1f);
    private static readonly Color ToastBg = new Color(0.02f, 0.08f, 0.1f, 0.97f);

    private sealed class HoloBtn
    {
        public RectTransform Rt;
        public Image Bg;
        public Text Label;
        public Action OnClick;
        public bool DangerStyle;
        public bool AccentStyle;
        public float PressAnim; // 1 → 0
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
        AttachToLeftHand();
        AnimateAppear(dt);
        AnimateButtons(dt);
        AnimateToast(dt);
        HandleTouch();
    }

    /// <summary>Cyberpunk in-holo notification (never Fusion popups).</summary>
    public static void Notify(string title, string body)
    {
        _toastTitleStr = string.IsNullOrEmpty(title) ? "GHOST" : title.ToUpperInvariant();
        _toastBodyStr = body ?? "";
        _status = _toastBodyStr;
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
            _panel = null;
            _toastRt = null;
            _toastBg = null;
            _toastTitle = null;
            _toastBody = null;
            _body = null;
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
            scaler.dynamicPixelsPerUnit = 12f;
            _root.AddComponent<GraphicRaycaster>();

            var crt = _root.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(440f, 560f);
            _root.transform.localScale = Vector3.one * 0.00052f;

            // Outer cyan frame
            var frame = MakeImage(_root.transform, "Frame", CyanDim);
            Stretch(frame.rectTransform);

            _panel = MakeImage(_root.transform, "Panel", Bg).rectTransform;
            Stretch(_panel);
            Inset(_panel, 5f);

            // Header
            var top = MakeImage(_panel, "Top", Panel).rectTransform;
            SetAnchors(top, 0f, 1f, 1f, 1f);
            top.pivot = new Vector2(0.5f, 1f);
            top.sizeDelta = new Vector2(0f, 64f);
            top.anchoredPosition = Vector2.zero;
            InsetX(top, 8f);

            var title = MakeText(top, "Title", "GHOST // BE PRIME", 22, Orange, TextAnchor.MiddleLeft);
            SetAnchors(title.rectTransform, 0f, 0.35f, 1f, 1f);
            title.rectTransform.offsetMin = new Vector2(14f, 0f);
            title.rectTransform.offsetMax = new Vector2(-14f, -4f);

            _headerSub = MakeText(top, "Sub", "TOUCH INTERFACE · INDEX FINGER", 12, Cyan, TextAnchor.MiddleLeft);
            SetAnchors(_headerSub.rectTransform, 0f, 0f, 1f, 0.42f);
            _headerSub.rectTransform.offsetMin = new Vector2(14f, 4f);
            _headerSub.rectTransform.offsetMax = new Vector2(-14f, 0f);

            // Accent line under header
            var line = MakeImage(_panel, "Line", Cyan).rectTransform;
            SetAnchors(line, 0f, 1f, 1f, 1f);
            line.pivot = new Vector2(0.5f, 1f);
            line.sizeDelta = new Vector2(0f, 2f);
            line.anchoredPosition = new Vector2(0f, -64f);
            InsetX(line, 12f);

            // Tabs
            var tabs = MakeImage(_panel, "Tabs", new Color(0.02f, 0.07f, 0.09f, 1f)).rectTransform;
            SetAnchors(tabs, 0f, 1f, 1f, 1f);
            tabs.pivot = new Vector2(0.5f, 1f);
            tabs.sizeDelta = new Vector2(0f, 50f);
            tabs.anchoredPosition = new Vector2(0f, -70f);
            InsetX(tabs, 8f);

            // Body
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(_panel, false);
            var bodyRt = bodyGo.AddComponent<RectTransform>();
            SetAnchors(bodyRt, 0f, 0f, 1f, 1f);
            bodyRt.offsetMin = new Vector2(10f, 12f);
            bodyRt.offsetMax = new Vector2(-10f, -128f);

            _body = MakeText(bodyRt, "BodyText", "", 15, TextCol, TextAnchor.UpperLeft);
            var brt = _body.rectTransform;
            SetAnchors(brt, 0f, 0.58f, 1f, 1f);
            brt.offsetMin = new Vector2(6f, 0f);
            brt.offsetMax = new Vector2(-6f, -4f);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Truncate;

            var btnHost = new GameObject("Buttons");
            btnHost.transform.SetParent(bodyRt, false);
            var bh = btnHost.AddComponent<RectTransform>();
            SetAnchors(bh, 0f, 0f, 1f, 0.58f);
            bh.offsetMin = Vector2.zero;
            bh.offsetMax = Vector2.zero;

            // Toast overlay (top of panel)
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

    private static void BuildToast()
    {
        var go = new GameObject("Toast");
        go.transform.SetParent(_panel, false);
        _toastRt = go.AddComponent<RectTransform>();
        SetAnchors(_toastRt, 0f, 1f, 1f, 1f);
        _toastRt.pivot = new Vector2(0.5f, 1f);
        _toastRt.sizeDelta = new Vector2(0f, 78f);
        _toastRt.anchoredPosition = new Vector2(0f, 90f); // start offscreen
        InsetX(_toastRt, 14f);

        _toastBg = go.AddComponent<Image>();
        _toastBg.color = ToastBg;

        // left accent bar
        var accent = MakeImage(_toastRt, "Accent", Orange).rectTransform;
        SetAnchors(accent, 0f, 0f, 0f, 1f);
        accent.pivot = new Vector2(0f, 0.5f);
        accent.sizeDelta = new Vector2(6f, 0f);
        accent.anchoredPosition = Vector2.zero;

        _toastTitle = MakeText(_toastRt, "TTitle", "", 14, Orange, TextAnchor.MiddleLeft);
        SetAnchors(_toastTitle.rectTransform, 0f, 0.48f, 1f, 1f);
        _toastTitle.rectTransform.offsetMin = new Vector2(16f, 0f);
        _toastTitle.rectTransform.offsetMax = new Vector2(-10f, -6f);

        _toastBody = MakeText(_toastRt, "TBody", "", 15, TextCol, TextAnchor.MiddleLeft);
        SetAnchors(_toastBody.rectTransform, 0f, 0f, 1f, 0.55f);
        _toastBody.rectTransform.offsetMin = new Vector2(16f, 8f);
        _toastBody.rectTransform.offsetMax = new Vector2(-10f, 0f);

        _toastT = 1f;
        ApplyToastVisual();
    }

    private static void AttachToLeftHand()
    {
        try
        {
            Hand hand = Player.LeftHand;
            if (hand == null || _root == null) return;
            Transform t = hand.transform;
            Vector3 pos = t.TransformPoint(new Vector3(0.03f, 0.02f, 0.09f));
            Quaternion rot = t.rotation * Quaternion.Euler(90f, 0f, -90f);
            _root.transform.SetPositionAndRotation(pos, rot);
        }
        catch { /* hand missing mid-load */ }
    }

    private static void AnimateAppear(float dt)
    {
        if (_appearT >= 1f || _root == null) return;
        _appearT = Mathf.Min(1f, _appearT + dt / 0.22f);
        float e = EaseOutCubic(_appearT);
        float s = 0.00052f * Mathf.Lerp(0.82f, 1f, e);
        _root.transform.localScale = Vector3.one * s;
    }

    private static void AnimateButtons(float dt)
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            HoloBtn b = _buttons[i];
            if (b?.Rt == null || b.Bg == null) continue;

            bool hover = i == _hoverIndex;
            b.HoverBlend = Mathf.MoveTowards(b.HoverBlend, hover ? 1f : 0f, dt * 10f);

            if (b.PressAnim > 0f)
                b.PressAnim = Mathf.Max(0f, b.PressAnim - dt / 0.16f);

            float punch = b.PressAnim > 0f
                ? 1f - Mathf.Sin((1f - b.PressAnim) * Mathf.PI) * 0.14f
                : 1f;
            float hoverScale = Mathf.Lerp(1f, 1.045f, b.HoverBlend);
            b.Rt.localScale = b.BaseScale * punch * hoverScale;

            Color baseCol = b.DangerStyle ? Danger : (b.AccentStyle ? OrangeDim : CyanDim);
            Color hot = b.DangerStyle ? new Color(1f, 0.5f, 0.55f) : CyanHot;
            Color flash = Color.Lerp(baseCol, Color.white, b.PressAnim * 0.65f);
            b.Bg.color = Color.Lerp(flash, hot, b.HoverBlend * (1f - b.PressAnim));
        }
    }

    private static void AnimateToast(float dt)
    {
        if (_toastRt == null) return;

        if (_toastT < 1f)
        {
            if (Time.unscaledTime < _toastHoldUntil)
            {
                // stay visible near 0
                _toastT = Mathf.MoveTowards(_toastT, 0f, dt / ToastIn);
            }
            else
            {
                _toastT = Mathf.MoveTowards(_toastT, 1f, dt / ToastOut);
            }
        }

        ApplyToastVisual();
    }

    private static void ApplyToastVisual()
    {
        if (_toastRt == null) return;
        float e = EaseOutCubic(1f - _toastT);
        _toastRt.anchoredPosition = new Vector2(0f, Mathf.Lerp(90f, -8f, e));
        if (_toastBg != null)
        {
            Color c = ToastBg;
            c.a = ToastBg.a * e;
            _toastBg.color = c;
        }
        if (_toastTitle != null)
        {
            _toastTitle.text = ">> " + _toastTitleStr;
            var c = Orange; c.a = e; _toastTitle.color = c;
        }
        if (_toastBody != null)
        {
            _toastBody.text = _toastBodyStr;
            var c = TextCol; c.a = e; _toastBody.color = c;
        }
    }

    private static void HandleTouch()
    {
        if (_buttons.Count == 0) return;
        if (!TryGetRightIndexTip(out Vector3 tip)) return;

        int best = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < _buttons.Count; i++)
        {
            HoloBtn b = _buttons[i];
            if (b?.Rt == null) continue;

            float dist = DistanceToButton(tip, b.Rt);
            float thresh = (i == _insideIndex) ? TouchExit : TouchEnter;
            if (dist < thresh && dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        _hoverIndex = best;

        // Enter → click once (poke)
        if (best != _insideIndex)
        {
            int prev = _insideIndex;
            _insideIndex = best;

            if (best >= 0 && prev != best && Time.unscaledTime >= _clickLockUntil)
            {
                FireButton(best);
            }
        }
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

    private static float DistanceToButton(Vector3 tip, RectTransform rt)
    {
        // Distance to button plane center, with planar clamp into rect
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        // 0=bl, 1=tl, 2=tr, 3=br
        Vector3 bl = corners[0];
        Vector3 tl = corners[1];
        Vector3 tr = corners[2];
        Vector3 right = tr - tl;
        Vector3 up = tl - bl;
        Vector3 normal = Vector3.Cross(right, up);
        if (normal.sqrMagnitude < 1e-8f)
            return Vector3.Distance(tip, rt.position);
        normal.Normalize();

        Vector3 center = (bl + tr) * 0.5f;
        float planeDist = Vector3.Dot(tip - center, normal);
        Vector3 projected = tip - normal * planeDist;

        // Barycentric-ish clamp into quad via local axes
        float w = right.magnitude;
        float h = up.magnitude;
        if (w < 1e-5f || h < 1e-5f)
            return Mathf.Abs(planeDist);

        Vector3 rN = right / w;
        Vector3 uN = up / h;
        Vector3 local = projected - bl;
        float u = Vector3.Dot(local, rN);
        float v = Vector3.Dot(local, uN);
        float cu = Mathf.Clamp(u, 0f, w);
        float cv = Mathf.Clamp(v, 0f, h);
        Vector3 closest = bl + rN * cu + uN * cv;
        return Vector3.Distance(tip, closest);
    }

    private static bool TryGetRightIndexTip(out Vector3 tip)
    {
        tip = default;
        try
        {
            RigManager rm = Player.RigManager;
            if (rm != null && rm.physicsRig != null && rm.physicsRig.artOutput != null)
            {
                Transform tipBone = rm.physicsRig.artOutput.artFingerRt13;
                if (tipBone != null)
                {
                    tip = tipBone.position;
                    return true;
                }
            }

            Hand hand = Player.RightHand;
            if (hand != null)
            {
                if (hand.palmPositionTransform != null)
                {
                    // Approximate index tip ahead of palm
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
        ClearButtons();
        if (_panel == null) return;

        AddTabButton(0, "NICK", Tab.Nick);
        AddTabButton(1, "LOBBY", Tab.Lobby);
        AddTabButton(2, "CUSTOM", Tab.Custom);

        Transform host = _panel.Find("Body/Buttons");
        if (host == null) return;

        switch (tab)
        {
            case Tab.Nick: BuildNick(host); break;
            case Tab.Lobby: BuildLobby(host); break;
            case Tab.Custom: BuildCustom(host); break;
        }

        if (_body != null)
            _body.text = BodyText(tab);

        if (_headerSub != null)
            _headerSub.text = tab == Tab.Lobby && !GhostLobby.IsHost
                ? "LOBBY LOCKED · HOST REQUIRED"
                : "TOUCH INTERFACE · INDEX FINGER";

        _insideIndex = -1;
        _hoverIndex = -1;
    }

    private static string BodyText(Tab tab)
    {
        switch (tab)
        {
            case Tab.Nick:
                return "IDENTITY LAYER\n· Invisible nametag\n· Presets\n· Clone player in session";
            case Tab.Lobby:
                return GhostLobby.IsHost
                    ? $"LOBBY SPOOF  [HOST]\nFakes online: {GhostLobby.Fakes.Count}\nInjected into lobby metadata"
                    : "LOBBY SPOOF\nHOST ONLY\nStart a lobby to unlock";
            case Tab.Custom:
                return $"MOD.IO CODE\n> {_keypad}_\nEnter avatar mod id · APPLY";
            default:
                return "";
        }
    }

    private static void BuildNick(Transform host)
    {
        float y = -8f;
        AddAction(host, ref y, "INVISIBLE", () =>
        {
            GhostIdentity.ApplyInvisible();
            Notify("NICK UPDATED", "Nametag → INVISIBLE");
        });
        AddAction(host, ref y, "PRESET: GHOST", () =>
        {
            GhostIdentity.ApplyPreset("GHOST");
            Notify("NICK UPDATED", "Identity → GHOST");
        });
        AddAction(host, ref y, "PRESET: ????", () =>
        {
            GhostIdentity.ApplyPreset("????");
            Notify("NICK UPDATED", "Identity → ????");
        });
        AddAction(host, ref y, "RESTORE REAL", () =>
        {
            GhostIdentity.Restore();
            Notify("IDENTITY RESTORED", "Original profile reloaded");
        }, danger: true);

        var players = GhostIdentity.ListSessionPlayers();
        int start = _playerPage * 3;
        for (int i = start; i < players.Count && i < start + 3; i++)
        {
            var entry = players[i];
            string label = "CLONE: " + Trim(entry.label, 14);
            PlayerID id = entry.id;
            string captured = entry.label;
            AddAction(host, ref y, label, () =>
            {
                GhostIdentity.CloneFromPlayer(id);
                Notify("CLONE COMPLETE", "Mirrored → " + Trim(captured, 20));
            });
        }
        if (players.Count > 3)
        {
            AddAction(host, ref y, "NEXT PAGE", () =>
            {
                _playerPage++;
                if (_playerPage * 3 >= players.Count) _playerPage = 0;
                QueueRebuild(Tab.Nick);
                Notify("PAGE", $"Players {( _playerPage + 1 )}");
            });
        }
    }

    private static void BuildLobby(Transform host)
    {
        float y = -8f;
        AddAction(host, ref y, "ADD FAKE PLAYER", () =>
        {
            int before = GhostLobby.Fakes.Count;
            GhostLobby.AddFake();
            if (GhostLobby.Fakes.Count > before)
            {
                Notify("LOBBY UPDATED", $"Fake added · total {GhostLobby.Fakes.Count}");
                QueueRebuild(Tab.Lobby);
            }
        });
        AddAction(host, ref y, "CLEAR FAKES", () =>
        {
            if (!GhostLobby.IsHost)
            {
                Notify("ACCESS DENIED", GhostLobby.EnsureHostOrError());
                return;
            }
            GhostLobby.ClearFakes();
            Notify("LOBBY UPDATED", "All fakes cleared");
            QueueRebuild(Tab.Lobby);
        }, danger: true);

        for (int i = 0; i < GhostLobby.AvatarPresets.Length && i < 4; i++)
        {
            string av = GhostLobby.AvatarPresets[i];
            AddAction(host, ref y, "AVATAR: " + av, () =>
            {
                GhostIdentity.ApplyAvatarMeta(av, -1);
                Notify("AVATAR META", "Profile icon → " + av);
            });
        }
    }

    private static void BuildCustom(Transform host)
    {
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "CLR", "0", "APPLY" };
        for (int i = 0; i < keys.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            string key = keys[i];
            float x = 24f + col * 122f;
            float yy = -10f - row * 50f;
            bool danger = key == "CLR";
            bool accent = key == "APPLY";
            AddActionAt(host, x, yy, 110f, 42f, key, () =>
            {
                if (key == "CLR")
                {
                    _keypad = "";
                    Notify("KEYPAD", "Cleared");
                }
                else if (key == "APPLY")
                {
                    if (int.TryParse(_keypad, out int modId) && modId > 0)
                    {
                        GhostIdentity.ApplyAvatarMeta("mod.io/" + modId, modId);
                        Notify("MOD.IO LINKED", "Avatar mod → " + modId);
                    }
                    else
                    {
                        Notify("INVALID CODE", "Enter a numeric mod.io id");
                    }
                }
                else if (_keypad.Length < 9)
                {
                    _keypad += key;
                }
                QueueRebuild(Tab.Custom);
            }, danger, accent);
        }
    }

    private static void AddTabButton(int index, string label, Tab tab)
    {
        var tabs = _panel.Find("Tabs");
        if (tabs == null) return;
        float w = 124f;
        float x = 14f + index * (w + 10f);
        bool active = _tab == tab;
        AddActionAt(tabs, x, -5f, w, 40f, label, () =>
        {
            QueueRebuild(tab);
        }, false, active);
    }

    private static void AddAction(Transform host, ref float y, string label, Action act, bool danger = false)
    {
        AddActionAt(host, 8f, y, 380f, 42f, label, act, danger, false);
        y -= 48f;
    }

    private static void AddActionAt(Transform host, float x, float y, float w, float h, string label, Action act, bool danger = false, bool accent = false)
    {
        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(host, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);

        var img = go.AddComponent<Image>();
        img.color = danger ? Danger : (accent ? OrangeDim : CyanDim);

        // thin inner border feel via child
        var border = MakeImage(rt, "Edge", Cyan).rectTransform;
        SetAnchors(border, 0f, 0f, 1f, 1f);
        border.offsetMin = Vector2.zero;
        border.offsetMax = Vector2.zero;
        var edgeImg = border.GetComponent<Image>();
        edgeImg.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f);
        // cover center so only edge shows — inset fill
        var fill = MakeImage(rt, "Fill", img.color).rectTransform;
        Stretch(fill);
        Inset(fill, 2f);
        fill.GetComponent<Image>().color = img.color;
        img.color = new Color(0f, 0f, 0f, 0f); // outer invisible, fill is visible

        var txt = MakeText(rt, "L", label, 15, TextCol, TextAnchor.MiddleCenter);
        Stretch(txt.rectTransform);
        Inset(txt.rectTransform, 4f);

        var btn = new HoloBtn
        {
            Rt = rt,
            Bg = fill.GetComponent<Image>(),
            Label = txt,
            OnClick = act,
            DangerStyle = danger,
            AccentStyle = accent,
            BaseScale = Vector3.one,
            PressAnim = 0f,
            HoverBlend = 0f
        };
        _buttons.Add(btn);
    }

    private static void ClearButtons()
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            if (_buttons[i]?.Rt != null)
                Object.Destroy(_buttons[i].Rt.gameObject);
        }
        _buttons.Clear();

        if (_panel == null) return;
        var tabs = _panel.Find("Tabs");
        if (tabs != null)
        {
            for (int i = tabs.childCount - 1; i >= 0; i--)
                Object.Destroy(tabs.GetChild(i).gameObject);
        }
        var host = _panel.Find("Body/Buttons");
        if (host != null)
        {
            for (int i = host.childCount - 1; i >= 0; i--)
                Object.Destroy(host.GetChild(i).gameObject);
        }
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
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
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
