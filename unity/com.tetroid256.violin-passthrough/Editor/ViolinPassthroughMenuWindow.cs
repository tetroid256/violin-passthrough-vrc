using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ViolinPassthrough.Editor
{
    /// <summary>
    /// アバターに Violin Passthrough の OSC 用メニューを追加するウィンドウ。
    /// Tools メニューとヒエラルキーの右クリック（GameObject メニュー）から開く。
    /// </summary>
    internal sealed class ViolinPassthroughMenuWindow : EditorWindow
    {
        private const string ToolsMenuPath = "Tools/Violin Passthrough/Add OSC Menu to Avatar";
        private const string GameObjectMenuPath = "GameObject/Violin Passthrough/Add OSC Menu";
        private const string PrefsKey = "ViolinPassthrough.MenuSettings.v1";

        [SerializeField] private VRCAvatarDescriptor avatar;
        [SerializeField] private MenuSettings settings;
        [SerializeField] private Vector2 scroll;

        // GUILayout は Layout イベントと他のイベントでコントロール数が一致している必要がある。
        // 表示するメッセージの数が入力の途中で変わらないよう、状態は Layout イベントのときだけ更新する
        [NonSerialized] private ValidationResult validation;
        [NonSerialized] private bool hasExisting;
        [NonSerialized] private bool playing;

        [MenuItem(ToolsMenuPath, false, 1000)]
        private static void OpenFromToolsMenu()
        {
            Open(MenuBuilder.FindAvatar(Selection.activeGameObject));
        }

        // 優先度 10 でヒエラルキーの右クリックメニューにも出る
        [MenuItem(GameObjectMenuPath, false, 10)]
        private static void OpenFromGameObjectMenu(MenuCommand command)
        {
            var target = command.context as GameObject;
            if (target == null) target = Selection.activeGameObject;
            Open(MenuBuilder.FindAvatar(target));
        }

        [MenuItem(GameObjectMenuPath, true, 10)]
        private static bool ValidateGameObjectMenu()
        {
            return MenuBuilder.FindAvatar(Selection.activeGameObject) != null;
        }

        private static void Open(VRCAvatarDescriptor target)
        {
            var window = GetWindow<ViolinPassthroughMenuWindow>(false, "Violin Passthrough", true);
            window.minSize = new Vector2(380f, 460f);
            if (target != null) window.avatar = target;
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Violin Passthrough");

            // ドメインリロード後はシリアライズされた値が残るので、未設定のときだけ前回の設定を読む
            if (settings == null) settings = LoadSettings();
            if (avatar == null) avatar = MenuBuilder.FindAvatar(Selection.activeGameObject);
        }

        private void OnSelectionChange()
        {
            // ヒエラルキーで別のアバター（またはその中）を選んだら、対象もそれに変える（別のアバターに追加しないように）
            var selected = MenuBuilder.FindAvatar(Selection.activeGameObject);
            if (selected == null || selected == avatar) return;
            avatar = selected;
            Repaint();
        }

        private void OnHierarchyChange()
        {
            Repaint();
        }

        private void OnGUI()
        {
            if (settings == null) settings = LoadSettings();
            if (settings.presets == null) settings.presets = MenuSettings.DefaultPresets();

            if (validation == null || Event.current.type == EventType.Layout)
            {
                hasExisting = MenuBuilder.FindExisting(avatar).Count > 0;
                validation = MenuBuilder.Validate(avatar, settings);
                playing = EditorApplication.isPlayingOrWillChangePlaymode;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Violin Passthrough OSC menu (Modular Avatar)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Adds a child object \"" + MenuBuilder.RootObjectName + "\" to the avatar with MA Parameters, MA Menu Installer " +
                "and MA Menu Item components. The avatar's own Expressions Menu / Parameters assets are not modified; " +
                "Modular Avatar merges the menu when the avatar is built. Delete the object to remove the menu.",
                MessageType.None);

            EditorGUILayout.Space();
            avatar = EditorGUILayout.ObjectField(
                new GUIContent("Avatar", "The GameObject with the VRC Avatar Descriptor."),
                avatar, typeof(VRCAvatarDescriptor), true) as VRCAvatarDescriptor;

            if (hasExisting)
            {
                EditorGUILayout.HelpBox(
                    "This avatar already has \"" + MenuBuilder.RootObjectName + "\". Add / Update deletes it and builds it again " +
                    "from the settings below: renamed toggles, icons and anything you added under it are lost (Ctrl+Z restores them).",
                    MessageType.Warning);
            }

            DrawMenuSection();
            DrawParameterSection();
            DrawPresetSection();

            DrawMessages(validation);
            DrawButtons();

            EditorGUILayout.EndScrollView();
        }

        private void DrawMenuSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Menu", EditorStyles.boldLabel);
            settings.menuLabel = EditorGUILayout.TextField(
                new GUIContent("Menu label", "Name of the submenu added to the avatar's root Expressions Menu."),
                settings.menuLabel);
            EditorGUILayout.LabelField("Items", "Passthrough (always), Preset submenu (if presets exist)");
            settings.includeAuto = EditorGUILayout.Toggle(
                new GUIContent("Auto", "Toggle for auto mode (show the windows only while the violin is raised)."),
                settings.includeAuto);
            settings.includeCalibrate = EditorGUILayout.Toggle(
                new GUIContent("Calibrate", "Toggle that starts the 5 s calibration countdown (turn off to cancel)."),
                settings.includeCalibrate);
            settings.includeDebug = EditorGUILayout.Toggle(
                new GUIContent("Debug", "Toggle that shows the mask windows in color for positioning."),
                settings.includeDebug);
            settings.includeViolin = EditorGUILayout.Toggle(
                new GUIContent("Violin", "Away / Hold / Play submenu for a violin model. The app sends the model state in a synced Int (8 synced bits) so other players see it. The model's animator is not generated by this tool."),
                settings.includeViolin);
        }

        private void DrawParameterSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Avatar parameters", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Must match \"osc.parameters\" in the app's config.json. Parameters are declared local-only " +
                "(not synced, 0 synced bits), not saved, default 0 / false, except \"Violin state\" which is synced (8 bits) " +
                "so other players see the violin model. Unchecked items only hide the menu control.",
                MessageType.None);
            settings.enabledParameter = EditorGUILayout.TextField(
                new GUIContent("Passthrough (Bool)", "osc.parameters.enabled"), settings.enabledParameter);
            settings.presetParameter = EditorGUILayout.TextField(
                new GUIContent("Preset (Int)", "osc.parameters.preset"), settings.presetParameter);
            settings.debugParameter = EditorGUILayout.TextField(
                new GUIContent("Debug (Bool)", "osc.parameters.debug"), settings.debugParameter);
            settings.autoParameter = EditorGUILayout.TextField(
                new GUIContent("Auto (Bool)", "osc.parameters.auto"), settings.autoParameter);
            settings.calibrateParameter = EditorGUILayout.TextField(
                new GUIContent("Calibrate (Bool)", "osc.parameters.calibrate"), settings.calibrateParameter);
            settings.autoMethodParameter = EditorGUILayout.TextField(
                new GUIContent("Auto method (Int)", "osc.parameters.autoMethod: 0 = left hand, 1 = left hand + head tilt, 2 = head tilt"), settings.autoMethodParameter);
            using (new EditorGUI.DisabledScope(!settings.includeViolin))
            {
                settings.violinParameter = EditorGUILayout.TextField(
                    new GUIContent("Violin (Int)", "osc.parameters.violin: menu choice 0 = away, 1 = hold, 2 = play"), settings.violinParameter);
                settings.violinStateParameter = EditorGUILayout.TextField(
                    new GUIContent("Violin state (Int, synced)", "osc.parameters.violinState: sent by the app (0 = away, 1 = held, 2 = playing). Synced; read it in the violin model's animator."), settings.violinStateParameter);
            }
        }

        private void DrawPresetSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "One toggle per preset. Index = position of the preset in config.json \"presets\" (0 = first). " +
                "With the default config: 0 = fingerboard, 1 = whole.",
                MessageType.None);

            var presets = settings.presets;
            var removeAt = -1;
            for (var i = 0; i < presets.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                presets[i].label = EditorGUILayout.TextField(presets[i].label);
                presets[i].index = EditorGUILayout.IntField(presets[i].index, GUILayout.Width(48f));
                if (GUILayout.Button("-", GUILayout.Width(24f))) removeAt = i;
                EditorGUILayout.EndHorizontal();
            }

            if (removeAt >= 0)
            {
                // 入力中の欄のフォーカスを外す（残すと、次の行の欄に古い文字が入る）
                GUI.FocusControl(null);
                presets.RemoveAt(removeAt);
            }

            if (GUILayout.Button("Add preset"))
            {
                var next = presets.Count == 0 ? 0 : presets.Max(p => p.index) + 1;
                presets.Add(new PresetEntry("Preset " + next, next));
            }
        }

        private static void DrawMessages(ValidationResult result)
        {
            if (result.Errors.Count == 0 && result.Warnings.Count == 0) return;

            EditorGUILayout.Space();
            foreach (var error in result.Errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            foreach (var warning in result.Warnings)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }
        }

        private void DrawButtons()
        {
            EditorGUILayout.Space();

            if (playing)
            {
                EditorGUILayout.HelpBox("Exit Play Mode to edit the avatar.", MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset to defaults"))
            {
                settings = new MenuSettings();
                GUI.FocusControl(null);
            }

            EditorGUI.BeginDisabledGroup(playing || !hasExisting || avatar == null || EditorUtility.IsPersistent(avatar));
            if (GUILayout.Button("Remove"))
            {
                RemoveMenu();

                // ダイアログやヒエラルキー変更の後はレイアウトが崩れるので、このフレームの描画を打ち切る
                GUIUtility.ExitGUI();
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(playing || validation.Errors.Count > 0);
            if (GUILayout.Button("Add / Update", GUILayout.Height(32f)))
            {
                AddMenu();
                GUIUtility.ExitGUI();
            }

            EditorGUI.EndDisabledGroup();
        }

        private void AddMenu()
        {
            // 表示中の検証結果は前の Layout 時点のものなので、押した時点の値で検証し直す
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var result = MenuBuilder.Validate(avatar, settings);
            if (result.Errors.Count > 0)
            {
                EditorUtility.DisplayDialog("Violin Passthrough", string.Join("\n", result.Errors), "OK");
                return;
            }

            GameObject root;
            try
            {
                root = MenuBuilder.Build(avatar, settings);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Violin Passthrough", "Failed to add the menu:\n" + e.Message, "OK");
                return;
            }

            SaveSettings(settings);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            ShowNotification(new GUIContent("Menu added"));
        }

        private void RemoveMenu()
        {
            if (avatar == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorUtility.DisplayDialog(
                    "Violin Passthrough",
                    "Remove \"" + MenuBuilder.RootObjectName + "\" from " + avatar.gameObject.name + "?",
                    "Remove", "Cancel"))
            {
                return;
            }

            try
            {
                MenuBuilder.Remove(avatar);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Violin Passthrough", "Failed to remove the menu:\n" + e.Message, "OK");
            }
        }

        private static MenuSettings LoadSettings()
        {
            var loaded = new MenuSettings();
            try
            {
                var json = EditorPrefs.GetString(PrefsKey, "");
                if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, loaded);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Violin Passthrough] Ignoring saved settings: " + e.Message);
                loaded = new MenuSettings();
            }

            if (loaded.presets == null) loaded.presets = MenuSettings.DefaultPresets();
            return loaded;
        }

        private static void SaveSettings(MenuSettings value)
        {
            try
            {
                EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(value));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Violin Passthrough] Could not save settings: " + e.Message);
            }
        }
    }
}
