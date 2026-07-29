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
/// Cyberpunk wrist hologram on the left hand. Static neon look — no looping FX.
/// Interact: aim right hand near a button + press A.
/// </summary>
public static class GhostHolo
{
    private enum Tab { Nick, Lobby, Custom }

    private static GameObject _root;
    private static Canvas _canvas;
    private static RectTransform _panel;
    private static Text _title;
    private static Text _toast;
    private static Text _body;
    private static readonly List<HoloBtn> _buttons = new List<HoloBtn>();
    private static Tab _tab = Tab.Nick;
    private static string _keypad = "";
    private static float _toastUntil;
    private static string _status = "GHOST ONLINE";
    private static int _playerPage;
    private static Font _font;
    private static bool _rebuildQueued;
    private static Tab _queuedTab;

    // Cyberpunk palette
    private static readonly Color Bg = new Color(0.02f, 0.05f, 0.07f, 0.92f);
    private static readonly Color Panel = new Color(0.04f, 0.10f, 0.14f, 0.95f);
    private static readonly Color Cyan = new Color(0.05f, 0.92f, 1f, 1f);
    private static readonly Color CyanDim = new Color(0.05f, 0.55f, 0.65f, 1f);
    private static readonly Color Orange = new Color(1f, 0.42f, 0.05f, 1f);
    private static readonly Color OrangeDim = new Color(0.75f, 0.28f, 0.04f, 1f);
    private static readonly Color TextCol = new Color(0.78f, 0.95f, 1f, 1f);
    private static readonly Color Danger = new Color(1f, 0.2f, 0.25f, 1f);

    private sealed class HoloBtn
    {
        public RectTransform Rt;
        public Image Bg;
        public Text Label;
        public Action OnClick;
        public bool DangerStyle;
    }

    public static void Tick()
    {
        if (!GhostMod.Enabled)
        {
            Destroy();
            return;
        }

        if (!GhostMod.FusionLoaded)
            return;

        Ensure();
        if (_root == null) return;

        if (_rebuildQueued)
        {
            _rebuildQueued = false;
            Rebuild(_queuedTab);
        }

        AttachToLeftHand();
        UpdateToast();
        HandleInput();
    }

    private static void QueueRebuild(Tab tab)
    {
        _queuedTab = tab;
        _rebuildQueued = true;
    }

    public static void ShowToast(string msg)
    {
        _toastUntil = Time.unscaledTime + 2.4f;
        if (_toast != null)
        {
            _toast.text = msg ?? "";
            _toast.color = Danger;
        }
        _status = msg ?? "";
    }

    public static void Destroy()
    {
        _buttons.Clear();
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _panel = null;
            _title = null;
            _toast = null;
            _body = null;
        }
    }

    private static void Ensure()
    {
        if (_root != null) return;
        try
        {
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _root = new GameObject("BE_PRIME_GHOST_HOLO");
            Object.DontDestroyOnLoad(_root);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 90;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            _root.AddComponent<GraphicRaycaster>();

            var crt = _root.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(420f, 520f);
            _root.transform.localScale = Vector3.one * 0.00055f;

            // Outer neon frame
            var frame = MakeImage(_root.transform, "Frame", CyanDim);
            Stretch(frame.rectTransform);
            frame.rectTransform.offsetMin = Vector2.zero;
            frame.rectTransform.offsetMax = Vector2.zero;

            _panel = MakeImage(_root.transform, "Panel", Bg).rectTransform;
            Stretch(_panel);
            Inset(_panel, 4f);

            // Top bar
            var top = MakeImage(_panel, "Top", Panel).rectTransform;
            SetAnchors(top, 0f, 1f, 1f, 1f);
            top.pivot = new Vector2(0.5f, 1f);
            top.sizeDelta = new Vector2(0f, 56f);
            top.anchoredPosition = Vector2.zero;
            InsetX(top, 8f);

            _title = MakeText(top, "Title", "GHOST // BE PRIME", 22, Orange, TextAnchor.MiddleLeft);
            var titleRt = _title.rectTransform;
            SetAnchors(titleRt, 0f, 0f, 1f, 1f);
            titleRt.offsetMin = new Vector2(12f, 0f);
            titleRt.offsetMax = new Vector2(-12f, 0f);

            // Tab strip
            var tabs = MakeImage(_panel, "Tabs", new Color(0.03f, 0.08f, 0.11f, 1f)).rectTransform;
            SetAnchors(tabs, 0f, 1f, 1f, 1f);
            tabs.pivot = new Vector2(0.5f, 1f);
            tabs.sizeDelta = new Vector2(0f, 48f);
            tabs.anchoredPosition = new Vector2(0f, -60f);
            InsetX(tabs, 8f);

            // Body
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(_panel, false);
            var bodyRt = bodyGo.AddComponent<RectTransform>();
            SetAnchors(bodyRt, 0f, 0f, 1f, 1f);
            bodyRt.offsetMin = new Vector2(10f, 54f);
            bodyRt.offsetMax = new Vector2(-10f, -116f);

            _body = MakeText(bodyRt, "BodyText", "", 16, TextCol, TextAnchor.UpperLeft);
            var brt = _body.rectTransform;
            SetAnchors(brt, 0f, 0.55f, 1f, 1f);
            brt.offsetMin = new Vector2(6f, 0f);
            brt.offsetMax = new Vector2(-6f, -4f);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Truncate;

            // Toast / status
            _toast = MakeText(_panel, "Toast", _status, 14, Cyan, TextAnchor.MiddleCenter);
            var tr = _toast.rectTransform;
            SetAnchors(tr, 0f, 0f, 1f, 0f);
            tr.pivot = new Vector2(0.5f, 0f);
            tr.sizeDelta = new Vector2(0f, 40f);
            tr.anchoredPosition = new Vector2(0f, 8f);
            InsetX(tr, 12f);

            // Button host
            var btnHost = new GameObject("Buttons");
            btnHost.transform.SetParent(bodyRt, false);
            var bh = btnHost.AddComponent<RectTransform>();
            SetAnchors(bh, 0f, 0f, 1f, 0.55f);
            bh.offsetMin = Vector2.zero;
            bh.offsetMax = Vector2.zero;

            Rebuild(_tab);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"Ghost holo build: {ex}");
            Destroy();
        }
    }

    private static void AttachToLeftHand()
    {
        try
        {
            Hand hand = Player.LeftHand;
            if (hand == null || _root == null) return;

            Transform t = hand.transform;
            _root.transform.SetParent(null, true);
            // Inner side of left wrist — readable when looking at your left arm
            Vector3 pos = t.TransformPoint(new Vector3(0.03f, 0.02f, 0.09f));
            Quaternion rot = t.rotation * Quaternion.Euler(90f, 0f, -90f);
            _root.transform.SetPositionAndRotation(pos, rot);
        }
        catch { /* hand may be missing mid-load */ }
    }

    private static void UpdateToast()
    {
        if (_toast == null) return;
        if (Time.unscaledTime > _toastUntil)
        {
            _toast.text = _status;
            _toast.color = Cyan;
        }
    }

    private static void HandleInput()
    {
        if (_buttons.Count == 0) return;

        Vector3 tip = Vector3.zero;
        bool click = false;
        try
        {
            if (Player.RightHand != null)
                tip = Player.RightHand.transform.position;
            var rc = Player.RightController;
            if (rc != null)
                click = rc.GetAButtonDown();
        }
        catch { return; }

        HoloBtn best = null;
        float bestDist = 0.055f;

        for (int i = 0; i < _buttons.Count; i++)
        {
            HoloBtn b = _buttons[i];
            if (b?.Rt == null || b.Bg == null) continue;
            Vector3 world = b.Rt.position;
            float d = Vector3.Distance(tip, world);
            bool hover = d < bestDist;
            Color baseCol = b.DangerStyle ? Danger : CyanDim;
            Color hot = b.DangerStyle ? new Color(1f, 0.45f, 0.45f) : Cyan;
            b.Bg.color = hover ? hot : baseCol;
            if (hover)
            {
                bestDist = d;
                best = b;
            }
        }

        if (click && best != null)
        {
            try { best.OnClick?.Invoke(); }
            catch (Exception ex) { MelonLogger.Warning($"Ghost btn: {ex.Message}"); }
        }
    }

    private static void Rebuild(Tab tab)
    {
        _tab = tab;
        ClearButtons();
        if (_panel == null) return;

        // Tab buttons under top
        AddTabButton(0, "NICK", Tab.Nick);
        AddTabButton(1, "LOBBY", Tab.Lobby);
        AddTabButton(2, "CUSTOM", Tab.Custom);

        Transform host = _panel.Find("Body/Buttons");
        if (host == null) return;

        switch (tab)
        {
            case Tab.Nick:
                BuildNick(host);
                break;
            case Tab.Lobby:
                BuildLobby(host);
                break;
            case Tab.Custom:
                BuildCustom(host);
                break;
        }

        if (_body != null)
            _body.text = BodyText(tab);
    }

    private static string BodyText(Tab tab)
    {
        switch (tab)
        {
            case Tab.Nick:
                return "IDENTITY LAYER\n· Invisible nametag\n· Presets\n· Clone a player in session\n  (name + avatar meta)";
            case Tab.Lobby:
                return GhostLobby.IsHost
                    ? $"LOBBY SPOOF  [HOST]\nFakes: {GhostLobby.Fakes.Count}\nInjected into lobby metadata."
                    : "LOBBY SPOOF\n<color=#FF3344>HOST ONLY</color>\nHost a lobby to unlock.";
            case Tab.Custom:
                return $"MOD.IO CODE\n{_keypad}\nEnter avatar mod id,\nthen APPLY.";
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
            _status = "NICK → INVISIBLE";
            ShowToast(_status);
        });
        AddAction(host, ref y, "PRESET: GHOST", () =>
        {
            GhostIdentity.ApplyPreset("GHOST");
            _status = "NICK → GHOST";
            ShowToast(_status);
        });
        AddAction(host, ref y, "PRESET: ????", () =>
        {
            GhostIdentity.ApplyPreset("????");
            _status = "NICK → ????";
            ShowToast(_status);
        });
        AddAction(host, ref y, "RESTORE REAL", () =>
        {
            GhostIdentity.Restore();
            _status = "IDENTITY RESTORED";
            ShowToast(_status);
        }, danger: true);

        var players = GhostIdentity.ListSessionPlayers();
        int start = _playerPage * 3;
        for (int i = start; i < players.Count && i < start + 3; i++)
        {
            var entry = players[i];
            string label = "CLONE: " + Trim(entry.label, 14);
            PlayerID id = entry.id;
            AddAction(host, ref y, label, () =>
            {
                GhostIdentity.CloneFromPlayer(id);
                _status = "CLONED " + Trim(entry.label, 18);
                ShowToast(_status);
            });
        }
        if (players.Count > 3)
        {
            AddAction(host, ref y, "NEXT PAGE", () =>
            {
                _playerPage++;
                if (_playerPage * 3 >= players.Count) _playerPage = 0;
                QueueRebuild(Tab.Nick);
            });
        }
    }

    private static void BuildLobby(Transform host)
    {
        float y = -8f;
        AddAction(host, ref y, "ADD FAKE PLAYER", () =>
        {
            GhostLobby.AddFake();
            _status = $"FAKES: {GhostLobby.Fakes.Count}";
            QueueRebuild(Tab.Lobby);
        });
        AddAction(host, ref y, "CLEAR FAKES", () =>
        {
            GhostLobby.ClearFakes();
            _status = "FAKES CLEARED";
            QueueRebuild(Tab.Lobby);
        }, danger: true);

        for (int i = 0; i < GhostLobby.AvatarPresets.Length && i < 4; i++)
        {
            string av = GhostLobby.AvatarPresets[i];
            AddAction(host, ref y, "AVATAR: " + av, () =>
            {
                GhostIdentity.ApplyAvatarMeta(av, -1);
                _status = "AVATAR META → " + av;
                ShowToast(_status);
            });
        }
    }

    private static void BuildCustom(Transform host)
    {
        // keypad 1-9, 0, CLR, APPLY
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "CLR", "0", "APPLY" };
        for (int i = 0; i < keys.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            string key = keys[i];
            float x = 20f + col * 120f;
            float yy = -12f - row * 48f;
            bool danger = key == "CLR";
            bool accent = key == "APPLY";
            AddActionAt(host, x, yy, 108f, 40f, key, () =>
            {
                if (key == "CLR")
                {
                    _keypad = "";
                }
                else if (key == "APPLY")
                {
                    if (int.TryParse(_keypad, out int modId) && modId > 0)
                    {
                        GhostIdentity.ApplyAvatarMeta("mod.io/" + modId, modId);
                        _status = "MOD.IO → " + modId;
                        ShowToast(_status);
                    }
                    else
                    {
                        ShowToast("INVALID CODE");
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
        float w = 120f;
        float x = 16f + index * (w + 10f);
        bool active = _tab == tab;
        AddActionAt(tabs, x, -4f, w, 40f, label, () => QueueRebuild(tab), false, active);
    }

    private static void AddAction(Transform host, ref float y, string label, Action act, bool danger = false)
    {
        AddActionAt(host, 8f, y, 360f, 40f, label, act, danger, false);
        y -= 46f;
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

        var txt = MakeText(rt, "L", label, 15, TextCol, TextAnchor.MiddleCenter);
        Stretch(txt.rectTransform);
        Inset(txt.rectTransform, 4f);

        _buttons.Add(new HoloBtn
        {
            Rt = rt,
            Bg = img,
            Label = txt,
            OnClick = act,
            DangerStyle = danger
        });
    }

    private static void ClearButtons()
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            if (_buttons[i]?.Rt != null)
                Object.Destroy(_buttons[i].Rt.gameObject);
        }
        _buttons.Clear();

        // Also wipe tab strip children except keep strip itself
        if (_panel != null)
        {
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
    }

    private static string Trim(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    }

    private static Image MakeImage(Transform parent, string name, Color col)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = col;
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
