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
/// Yellow cyberpunk hologram on the LEFT WRIST (side), not in the palm/fingers.
/// Poke with right index fingertip. Short one-shot anims only.
/// </summary>
public static class GhostHolo
{
    private enum Tab { Nick, Lobby, Custom }

    // World size target: ~6.5cm x ~13cm (tall strip along wrist)
    private const float CanvasW = 160f;
    private const float CanvasH = 320f;
    private const float WorldScale = 0.00040f; // 160→6.4cm, 320→12.8cm

    private static GameObject _root;
    private static RectTransform _canvasRt;
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
    private static int _playerPage;
    private static Font _font;

    private static bool _rebuildQueued;
    private static Tab _queuedTab;

    private static int _hoverIndex = -1;
    private static int _insideIndex = -1;
    private static float _clickLockUntil;
    private const float ClickCooldown = 0.38f;
    private const float TouchEnter = 0.032f;
    private const float TouchExit = 0.042f;

    private static float _toastT = 1f;
    private static float _toastHoldUntil;
    private static string _toastTitleStr = "";
    private static string _toastBodyStr = "";
    private const float ToastIn = 0.12f;
    private const float ToastHold = 1.7f;
    private const float ToastOut = 0.2f;

    private static float _appearT = 1f;
    private static float _baseScale = WorldScale;

    // Cyberpunk yellow
    private static readonly Color Bg = new Color(0.06f, 0.05f, 0.01f, 0.92f);
    private static readonly Color Panel = new Color(0.10f, 0.08f, 0.02f, 0.95f);
    private static readonly Color Yellow = new Color(1f, 0.86f, 0.12f, 1f);
    private static readonly Color YellowDim = new Color(0.55f, 0.42f, 0.05f, 1f);
    private static readonly Color YellowHot = new Color(1f, 0.95f, 0.45f, 1f);
    private static readonly Color YellowDeep = new Color(0.85f, 0.55f, 0.02f, 1f);
    private static readonly Color TextCol = new Color(1f, 0.92f, 0.55f, 1f);
    private static readonly Color Danger = new Color(1f, 0.28f, 0.12f, 1f);
    private static readonly Color ToastBg = new Color(0.08f, 0.06f, 0.01f, 0.97f);

    private sealed class HoloBtn
    {
        public RectTransform Rt;
        public Image Bg;
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
        AttachToLeftWrist();
        AnimateAppear(dt);
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
            scaler.dynamicPixelsPerUnit = 14f;
            _root.AddComponent<GraphicRaycaster>();

            _canvasRt = _root.GetComponent<RectTransform>();
            _canvasRt.sizeDelta = new Vector2(CanvasW, CanvasH);
            _baseScale = WorldScale;
            _root.transform.localScale = Vector3.one * _baseScale;

            var frame = MakeImage(_root.transform, "Frame", YellowDim);
            Stretch(frame.rectTransform);

            _panel = MakeImage(_root.transform, "Panel", Bg).rectTransform;
            Stretch(_panel);
            Inset(_panel, 3f);

            // Header
            var top = MakeImage(_panel, "Top", Panel).rectTransform;
            SetAnchors(top, 0f, 1f, 1f, 1f);
            top.pivot = new Vector2(0.5f, 1f);
            top.sizeDelta = new Vector2(0f, 42f);
            top.anchoredPosition = Vector2.zero;
            InsetX(top, 4f);

            var title = MakeText(top, "Title", "GHOST", 18, Yellow, TextAnchor.MiddleLeft);
            SetAnchors(title.rectTransform, 0f, 0.4f, 1f, 1f);
            title.rectTransform.offsetMin = new Vector2(8f, 0f);
            title.rectTransform.offsetMax = new Vector2(-6f, -2f);

            _headerSub = MakeText(top, "Sub", "WRIST LINK", 10, YellowDeep, TextAnchor.MiddleLeft);
            SetAnchors(_headerSub.rectTransform, 0f, 0f, 1f, 0.48f);
            _headerSub.rectTransform.offsetMin = new Vector2(8f, 2f);
            _headerSub.rectTransform.offsetMax = new Vector2(-6f, 0f);

            var line = MakeImage(_panel, "Line", Yellow).rectTransform;
            SetAnchors(line, 0f, 1f, 1f, 1f);
            line.pivot = new Vector2(0.5f, 1f);
            line.sizeDelta = new Vector2(0f, 2f);
            line.anchoredPosition = new Vector2(0f, -42f);
            InsetX(line, 6f);

            var tabs = MakeImage(_panel, "Tabs", new Color(0.08f, 0.06f, 0.01f, 1f)).rectTransform;
            SetAnchors(tabs, 0f, 1f, 1f, 1f);
            tabs.pivot = new Vector2(0.5f, 1f);
            tabs.sizeDelta = new Vector2(0f, 34f);
            tabs.anchoredPosition = new Vector2(0f, -46f);
            InsetX(tabs, 4f);

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(_panel, false);
            var bodyRt = bodyGo.AddComponent<RectTransform>();
            SetAnchors(bodyRt, 0f, 0f, 1f, 1f);
            bodyRt.offsetMin = new Vector2(6f, 8f);
            bodyRt.offsetMax = new Vector2(-6f, -84f);

            _body = MakeText(bodyRt, "BodyText", "", 11, TextCol, TextAnchor.UpperLeft);
            var brt = _body.rectTransform;
            SetAnchors(brt, 0f, 0.62f, 1f, 1f);
            brt.offsetMin = new Vector2(2f, 0f);
            brt.offsetMax = new Vector2(-2f, -2f);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Truncate;

            var btnHost = new GameObject("Buttons");
            btnHost.transform.SetParent(bodyRt, false);
            var bh = btnHost.AddComponent<RectTransform>();
            SetAnchors(bh, 0f, 0f, 1f, 0.62f);
            bh.offsetMin = Vector2.zero;
            bh.offsetMax = Vector2.zero;

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
        _toastRt.sizeDelta = new Vector2(0f, 52f);
        _toastRt.anchoredPosition = new Vector2(0f, 60f);
        InsetX(_toastRt, 6f);

        _toastBg = go.AddComponent<Image>();
        _toastBg.color = ToastBg;

        var accent = MakeImage(_toastRt, "Accent", Yellow).rectTransform;
        SetAnchors(accent, 0f, 0f, 0f, 1f);
        accent.pivot = new Vector2(0f, 0.5f);
        accent.sizeDelta = new Vector2(4f, 0f);

        _toastTitle = MakeText(_toastRt, "TTitle", "", 11, Yellow, TextAnchor.MiddleLeft);
        SetAnchors(_toastTitle.rectTransform, 0f, 0.48f, 1f, 1f);
        _toastTitle.rectTransform.offsetMin = new Vector2(10f, 0f);
        _toastTitle.rectTransform.offsetMax = new Vector2(-4f, -3f);

        _toastBody = MakeText(_toastRt, "TBody", "", 12, TextCol, TextAnchor.MiddleLeft);
        SetAnchors(_toastBody.rectTransform, 0f, 0f, 1f, 0.55f);
        _toastBody.rectTransform.offsetMin = new Vector2(10f, 4f);
        _toastBody.rectTransform.offsetMax = new Vector2(-4f, 0f);

        _toastT = 1f;
        ApplyToastVisual();
    }

    /// <summary>
    /// Sit on the LEFT WRIST, outer/side face — away from palm & fingers.
    /// </summary>
    private static void AttachToLeftWrist()
    {
        try
        {
            Hand hand = Player.LeftHand;
            if (hand == null || _root == null) return;

            Transform palm = hand.palmPositionTransform != null ? hand.palmPositionTransform : hand.transform;

            // Palm local: +Z ~ fingers, -Z ~ wrist, ±X ~ side of hand.
            // Pull back to wrist, shift to the outer side of the left hand.
            Vector3 local =
                new Vector3(-0.055f, 0.01f, -0.075f); // side + toward wrist
            Vector3 pos = palm.TransformPoint(local);

            // Face the panel outward from the wrist side so you can read it
            // when looking at your left wrist from the outside.
            Vector3 awayFromPalm = -palm.up;          // out of palm
            Vector3 alongWrist = -palm.forward;       // toward forearm
            if (awayFromPalm.sqrMagnitude < 0.001f) awayFromPalm = palm.right;
            Vector3 face = Vector3.Cross(alongWrist, palm.right);
            if (face.sqrMagnitude < 0.001f) face = awayFromPalm;
            face.Normalize();

            // Panel faces the user looking at the outer wrist
            Quaternion rot = Quaternion.LookRotation(face, alongWrist);
            // Flip so text isn't mirrored for left-wrist viewing
            rot *= Quaternion.Euler(0f, 180f, 90f);

            _root.transform.SetPositionAndRotation(pos, rot);
        }
        catch { /* hand missing */ }
    }

    private static void AnimateAppear(float dt)
    {
        if (_appearT >= 1f || _root == null) return;
        _appearT = Mathf.Min(1f, _appearT + dt / 0.18f);
        float e = EaseOutCubic(_appearT);
        float s = _baseScale * Mathf.Lerp(0.85f, 1f, e);
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
                b.PressAnim = Mathf.Max(0f, b.PressAnim - dt / 0.15f);

            float punch = b.PressAnim > 0f
                ? 1f - Mathf.Sin((1f - b.PressAnim) * Mathf.PI) * 0.12f
                : 1f;
            float hoverScale = Mathf.Lerp(1f, 1.04f, b.HoverBlend);
            b.Rt.localScale = b.BaseScale * punch * hoverScale;

            Color baseCol = b.DangerStyle ? Danger : (b.AccentStyle ? YellowDeep : YellowDim);
            Color hot = b.DangerStyle ? new Color(1f, 0.5f, 0.3f) : YellowHot;
            Color flash = Color.Lerp(baseCol, Yellow, b.PressAnim * 0.7f);
            b.Bg.color = Color.Lerp(flash, hot, b.HoverBlend * (1f - b.PressAnim));
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
        _toastRt.anchoredPosition = new Vector2(0f, Mathf.Lerp(56f, -4f, e));
        if (_toastBg != null)
        {
            Color c = ToastBg; c.a = ToastBg.a * e; _toastBg.color = c;
        }
        if (_toastTitle != null)
        {
            _toastTitle.text = ">> " + _toastTitleStr;
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
        if (best != _insideIndex)
        {
            int prev = _insideIndex;
            _insideIndex = best;
            if (best >= 0 && prev != best && Time.unscaledTime >= _clickLockUntil)
                FireButton(best);
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
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector3 bl = corners[0], tl = corners[1], tr = corners[2];
        Vector3 right = tr - tl;
        Vector3 up = tl - bl;
        Vector3 normal = Vector3.Cross(right, up);
        if (normal.sqrMagnitude < 1e-8f)
            return Vector3.Distance(tip, rt.position);
        normal.Normalize();

        Vector3 center = (bl + tr) * 0.5f;
        float planeDist = Vector3.Dot(tip - center, normal);
        Vector3 projected = tip - normal * planeDist;

        float w = right.magnitude;
        float h = up.magnitude;
        if (w < 1e-5f || h < 1e-5f) return Mathf.Abs(planeDist);

        Vector3 rN = right / w;
        Vector3 uN = up / h;
        Vector3 local = projected - bl;
        float u = Mathf.Clamp(Vector3.Dot(local, rN), 0f, w);
        float v = Mathf.Clamp(Vector3.Dot(local, uN), 0f, h);
        Vector3 closest = bl + rN * u + uN * v;
        return Vector3.Distance(tip, closest);
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
        ClearButtons();
        if (_panel == null) return;

        AddTabButton(0, "NICK", Tab.Nick);
        AddTabButton(1, "LOBBY", Tab.Lobby);
        AddTabButton(2, "CODE", Tab.Custom);

        Transform host = _panel.Find("Body/Buttons");
        if (host == null) return;

        switch (tab)
        {
            case Tab.Nick: BuildNick(host); break;
            case Tab.Lobby: BuildLobby(host); break;
            case Tab.Custom: BuildCustom(host); break;
        }

        if (_body != null) _body.text = BodyText(tab);
        if (_headerSub != null)
            _headerSub.text = tab == Tab.Lobby && !GhostLobby.IsHost ? "HOST ONLY" : "WRIST LINK";

        _insideIndex = -1;
        _hoverIndex = -1;
    }

    private static string BodyText(Tab tab)
    {
        switch (tab)
        {
            case Tab.Nick:
                return "IDENTITY\nHide / rename / clone\na player in session";
            case Tab.Lobby:
                return GhostLobby.IsHost
                    ? $"LOBBY  HOST\nFakes: {GhostLobby.Fakes.Count}"
                    : "LOBBY\nHost a server\nto unlock";
            case Tab.Custom:
                return $"MOD.IO\n> {_keypad}_";
            default:
                return "";
        }
    }

    private static void BuildNick(Transform host)
    {
        float y = -4f;
        AddAction(host, ref y, "HIDE NAME", () =>
        {
            GhostIdentity.ApplyInvisible();
            Notify("NICK", "Name hidden");
        });
        AddAction(host, ref y, "NAME: GHOST", () =>
        {
            GhostIdentity.ApplyPreset("GHOST");
            Notify("NICK", "Set to GHOST");
        });
        AddAction(host, ref y, "NAME: UNKNOWN", () =>
        {
            GhostIdentity.ApplyPreset("UNKNOWN");
            Notify("NICK", "Set to UNKNOWN");
        });
        AddAction(host, ref y, "NAME: ANON", () =>
        {
            GhostIdentity.ApplyPreset("ANON");
            Notify("NICK", "Set to ANON");
        });
        AddAction(host, ref y, "RESTORE", () =>
        {
            GhostIdentity.Restore();
            Notify("RESTORE", "Real profile back");
        }, danger: true);

        var players = GhostIdentity.ListSessionPlayers();
        int start = _playerPage * 2;
        for (int i = start; i < players.Count && i < start + 2; i++)
        {
            var entry = players[i];
            string label = "COPY " + Trim(entry.label, 10);
            PlayerID id = entry.id;
            string captured = entry.label;
            AddAction(host, ref y, label, () =>
            {
                GhostIdentity.CloneFromPlayer(id);
                Notify("CLONE", Trim(captured, 16));
            });
        }
        if (players.Count > 2)
        {
            AddAction(host, ref y, "MORE PLAYERS", () =>
            {
                _playerPage++;
                if (_playerPage * 2 >= players.Count) _playerPage = 0;
                QueueRebuild(Tab.Nick);
            });
        }
    }

    private static void BuildLobby(Transform host)
    {
        float y = -4f;
        AddAction(host, ref y, "ADD FAKE", () =>
        {
            int before = GhostLobby.Fakes.Count;
            GhostLobby.AddFake();
            if (GhostLobby.Fakes.Count > before)
            {
                Notify("LOBBY", $"Fake +1 · {GhostLobby.Fakes.Count}");
                QueueRebuild(Tab.Lobby);
            }
        });
        AddAction(host, ref y, "CLEAR FAKES", () =>
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

        for (int i = 0; i < GhostLobby.AvatarPresets.Length && i < 4; i++)
        {
            string av = GhostLobby.AvatarPresets[i];
            AddAction(host, ref y, "ICON: " + av.ToUpperInvariant(), () =>
            {
                GhostIdentity.ApplyAvatarMeta(av, -1);
                Notify("AVATAR", "Icon → " + av);
            });
        }
    }

    private static void BuildCustom(Transform host)
    {
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "CLR", "0", "OK" };
        for (int i = 0; i < keys.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            string key = keys[i];
            float x = 6f + col * 48f;
            float yy = -4f - row * 36f;
            bool danger = key == "CLR";
            bool accent = key == "OK";
            AddActionAt(host, x, yy, 44f, 32f, key, () =>
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

    private static void AddTabButton(int index, string label, Tab tab)
    {
        var tabs = _panel.Find("Tabs");
        if (tabs == null) return;
        float w = 46f;
        float x = 6f + index * (w + 4f);
        bool active = _tab == tab;
        AddActionAt(tabs, x, -3f, w, 28f, label, () => QueueRebuild(tab), false, active);
    }

    private static void AddAction(Transform host, ref float y, string label, Action act, bool danger = false)
    {
        AddActionAt(host, 2f, y, 140f, 28f, label, act, danger, false);
        y -= 32f;
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

        var outer = go.AddComponent<Image>();
        outer.color = new Color(0f, 0f, 0f, 0f);
        outer.raycastTarget = false;

        var edge = MakeImage(rt, "Edge", Yellow).rectTransform;
        Stretch(edge);
        edge.GetComponent<Image>().color = new Color(Yellow.r, Yellow.g, Yellow.b, 0.4f);

        var fill = MakeImage(rt, "Fill", YellowDim).rectTransform;
        Stretch(fill);
        Inset(fill, 1.5f);
        var fillImg = fill.GetComponent<Image>();
        fillImg.color = danger ? Danger : (accent ? YellowDeep : YellowDim);

        var txt = MakeText(rt, "L", label, 11, TextCol, TextAnchor.MiddleCenter);
        Stretch(txt.rectTransform);
        Inset(txt.rectTransform, 2f);

        _buttons.Add(new HoloBtn
        {
            Rt = rt,
            Bg = fillImg,
            Label = txt,
            OnClick = act,
            DangerStyle = danger,
            AccentStyle = accent,
            BaseScale = Vector3.one
        });
    }

    private static void ClearButtons()
    {
        for (int i = 0; i < _buttons.Count; i++)
            if (_buttons[i]?.Rt != null) Object.Destroy(_buttons[i].Rt.gameObject);
        _buttons.Clear();

        if (_panel == null) return;
        var tabs = _panel.Find("Tabs");
        if (tabs != null)
            for (int i = tabs.childCount - 1; i >= 0; i--)
                Object.Destroy(tabs.GetChild(i).gameObject);
        var host = _panel.Find("Body/Buttons");
        if (host != null)
            for (int i = host.childCount - 1; i >= 0; i--)
                Object.Destroy(host.GetChild(i).gameObject);
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
