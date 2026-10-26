using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ViolinPassthrough.Editor
{
    /// <summary>プリセット 1 件分のメニュー項目。index はアプリの config.json の presets の並び順（0 始まり）。</summary>
    [Serializable]
    internal sealed class PresetEntry
    {
        public string label = "";
        public int index;

        public PresetEntry()
        {
        }

        public PresetEntry(string label, int index)
        {
            this.label = label;
            this.index = index;
        }
    }

    /// <summary>ウィンドウで編集する設定。パラメータ名の既定値はアプリの config.json（osc.parameters）と同じ。</summary>
    [Serializable]
    internal sealed class MenuSettings
    {
        public string menuLabel = MenuBuilder.DefaultMenuLabel;
        public string enabledParameter = "HandPassthrough";
        public string presetParameter = "HandPassthroughPreset";
        public string debugParameter = "HandPassthroughDebug";
        public string autoParameter = "HandPassthroughAuto";
        public string calibrateParameter = "HandPassthroughCalibrate";
        public bool includeAuto = true;
        public bool includeCalibrate = true;
        public bool includeDebug = true;
        public List<PresetEntry> presets = DefaultPresets();

        internal static List<PresetEntry> DefaultPresets()
        {
            return new List<PresetEntry> { new PresetEntry("Fingerboard", 0), new PresetEntry("Whole", 1) };
        }

        /// <summary>宣言するパラメータ（表示名, パラメータ名, 型）。メニューに出さない項目のパラメータも宣言する。</summary>
        internal IEnumerable<(string role, string name, ParameterSyncType type)> Parameters()
        {
            yield return ("Passthrough", enabledParameter, ParameterSyncType.Bool);
            yield return ("Preset", presetParameter, ParameterSyncType.Int);
            yield return ("Debug", debugParameter, ParameterSyncType.Bool);
            yield return ("Auto", autoParameter, ParameterSyncType.Bool);
            yield return ("Calibrate", calibrateParameter, ParameterSyncType.Bool);
        }
    }

    /// <summary>検証結果。errors が 1 件でもあれば追加しない。</summary>
    internal sealed class ValidationResult
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// アバター直下に MA Parameters / MA Menu Installer / MA Menu Item を持つ子オブジェクトを作る。
    /// アバター本体の Expressions Menu / Parameters アセットには触らない（ビルド時に MA が合成する）。
    /// </summary>
    internal static class MenuBuilder
    {
        internal const string RootObjectName = "ViolinPassthrough OSC Menu";
        internal const string DefaultMenuLabel = "Violin Passthrough";
        internal const string UndoName = "Add Violin Passthrough OSC Menu";
        internal const int MaxPresetIndex = 255;

        // OSC アドレスに使えない文字（OSC 1.0 の予約文字）と空白
        private static readonly char[] InvalidParameterChars = { ' ', '\t', '#', '*', ',', '?', '[', ']', '{', '}' };

        /// <summary>選択中のオブジェクトから親方向にたどって最も近い VRCAvatarDescriptor を返す（非アクティブも対象）。</summary>
        internal static VRCAvatarDescriptor FindAvatar(GameObject selected)
        {
            for (var t = selected != null ? selected.transform : null; t != null; t = t.parent)
            {
                var descriptor = t.GetComponent<VRCAvatarDescriptor>();
                if (descriptor != null) return descriptor;
            }

            return null;
        }

        /// <summary>このツールが以前作ったオブジェクト（アバター直下、名前一致、MA Parameters と MA Menu Installer を持つ）。</summary>
        internal static List<GameObject> FindExisting(VRCAvatarDescriptor avatar)
        {
            var result = new List<GameObject>();
            if (avatar == null) return result;

            foreach (Transform child in avatar.transform)
            {
                if (child.name == RootObjectName
                    && child.GetComponent<ModularAvatarParameters>() != null
                    && child.GetComponent<ModularAvatarMenuInstaller>() != null)
                {
                    result.Add(child.gameObject);
                }
            }

            return result;
        }

        /// <summary>プレハブの中身（オーバーライドでない部分）は削除できないので事前に判定する。</summary>
        internal static bool CanRemove(GameObject go)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(go)) return true;
            if (PrefabUtility.IsAddedGameObjectOverride(go)) return true;
            if (!PrefabUtility.IsOutermostPrefabInstanceRoot(go)) return false;

            var parent = go.transform.parent;
            return parent == null || !PrefabUtility.IsPartOfPrefabInstance(parent);
        }

        internal static ValidationResult Validate(VRCAvatarDescriptor avatar, MenuSettings s)
        {
            var r = new ValidationResult();

            if (avatar == null)
            {
                r.Errors.Add("Select an avatar (a GameObject with a VRC Avatar Descriptor).");
            }
            else if (EditorUtility.IsPersistent(avatar))
            {
                r.Errors.Add("The avatar is a prefab asset. Select the avatar in the scene, or open the prefab in Prefab Mode.");
            }

            if (string.IsNullOrWhiteSpace(s.menuLabel))
            {
                r.Errors.Add("Menu label must not be empty.");
            }

            var parameters = s.Parameters().ToList();
            foreach (var (role, name, _) in parameters)
            {
                var error = ValidateParameterName(name);
                if (error != null) r.Errors.Add($"{role} parameter: {error}");
            }

            foreach (var group in parameters.Where(p => !string.IsNullOrEmpty(p.name)).GroupBy(p => p.name, StringComparer.Ordinal))
            {
                if (group.Count() > 1)
                {
                    r.Errors.Add($"Parameter name \"{group.Key}\" is used more than once ({string.Join(", ", group.Select(p => p.role))}).");
                }
            }

            ValidatePresets(s, r);

            if (avatar != null)
            {
                var existing = FindExisting(avatar);
                foreach (var go in existing)
                {
                    if (!CanRemove(go))
                    {
                        r.Errors.Add($"The existing \"{RootObjectName}\" is part of a prefab and cannot be replaced here. Open the prefab and update or delete it there (or unpack the prefab).");
                    }
                }

                CheckAvatarExpressionParameters(avatar, parameters, r);
                CheckOtherDeclarations(avatar, existing, parameters, r);
            }

            return r;
        }

        private static string ValidateParameterName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "name must not be empty.";
            if (name.Trim().Length != name.Length) return $"\"{name}\" must not start or end with spaces.";
            if (name.IndexOfAny(InvalidParameterChars) >= 0) return $"\"{name}\" must not contain spaces or any of # * , ? [ ] {{ }}.";
            if (name.StartsWith("/", StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal)) return $"\"{name}\" must not start or end with '/'.";
            return null;
        }

        private static void ValidatePresets(MenuSettings s, ValidationResult r)
        {
            if (s.presets == null || s.presets.Count == 0)
            {
                r.Warnings.Add("No presets: the \"Preset\" submenu will be omitted (the preset parameter is still declared).");
                return;
            }

            for (var i = 0; i < s.presets.Count; i++)
            {
                var p = s.presets[i];
                var label = p.label ?? "";
                if (string.IsNullOrWhiteSpace(label))
                {
                    r.Errors.Add($"Preset #{i + 1}: label must not be empty.");
                }
                else if (label.Contains("/"))
                {
                    // ラベルはそのまま GameObject 名にするので、パス区切りになる '/' は避ける
                    r.Errors.Add($"Preset \"{label}\": label must not contain '/'.");
                }

                if (p.index < 0 || p.index > MaxPresetIndex)
                {
                    r.Errors.Add($"Preset \"{label}\": index must be between 0 and {MaxPresetIndex}.");
                }
            }

            foreach (var group in s.presets.GroupBy(p => p.index))
            {
                if (group.Count() > 1)
                {
                    r.Errors.Add($"Preset index {group.Key} is used more than once.");
                }
            }
        }

        /// <summary>
        /// アバター自身の Expression Parameters に同名があると MA はそちらと合成する
        /// （Synced / Saved はどちらかが ON なら ON、型が違うとビルドエラー）。
        /// </summary>
        private static void CheckAvatarExpressionParameters(
            VRCAvatarDescriptor avatar,
            List<(string role, string name, ParameterSyncType type)> parameters,
            ValidationResult r)
        {
            var asset = avatar.expressionParameters;
            if (asset == null || asset.parameters == null) return;

            foreach (var (_, name, type) in parameters)
            {
                var declared = asset.parameters.FirstOrDefault(p => p != null && p.name == name);
                if (declared == null) continue;

                var expected = type == ParameterSyncType.Int
                    ? VRCExpressionParameters.ValueType.Int
                    : VRCExpressionParameters.ValueType.Bool;
                if (declared.valueType != expected)
                {
                    r.Warnings.Add($"\"{name}\" is already in the avatar's Expression Parameters as {declared.valueType} (expected {expected}). Modular Avatar will report a type conflict when building. Remove it from \"{asset.name}\".");
                }
                else if (declared.networkSynced || declared.saved)
                {
                    r.Warnings.Add($"\"{name}\" is already in the avatar's Expression Parameters with Synced/Saved enabled, so it will stay synced/saved. Remove it from \"{asset.name}\" to make it local-only.");
                }
            }
        }

        /// <summary>置き換え対象以外の MA Parameters が同じ名前を宣言していたら（重複メニューの可能性）知らせる。</summary>
        private static void CheckOtherDeclarations(
            VRCAvatarDescriptor avatar,
            List<GameObject> existing,
            List<(string role, string name, ParameterSyncType type)> parameters,
            ValidationResult r)
        {
            var names = new HashSet<string>(parameters.Select(p => p.name), StringComparer.Ordinal);
            foreach (var component in avatar.GetComponentsInChildren<ModularAvatarParameters>(true))
            {
                if (existing.Any(go => component.transform.IsChildOf(go.transform))) continue;
                if (component.parameters == null) continue;

                var hits = component.parameters
                    .Where(p => !p.isPrefix && p.nameOrPrefix != null && names.Contains(p.nameOrPrefix))
                    .Select(p => p.nameOrPrefix)
                    .Distinct()
                    .ToList();
                if (hits.Count > 0)
                {
                    r.Warnings.Add($"\"{component.gameObject.name}\" also declares {string.Join(", ", hits)}. If it is an older copy of this menu, delete it to avoid duplicate menus.");
                }
            }
        }

        /// <summary>
        /// 既存のメニューオブジェクトを削除して作り直す。Undo 1 回で元に戻せる。
        /// 呼び出し前に Validate でエラーがないことを確認しておくこと。
        /// </summary>
        internal static GameObject Build(VRCAvatarDescriptor avatar, MenuSettings s)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            var undoGroup = Undo.GetCurrentGroup();

            try
            {
                var siblingIndex = -1;
                foreach (var old in FindExisting(avatar))
                {
                    if (siblingIndex < 0) siblingIndex = old.transform.GetSiblingIndex();
                    Undo.DestroyObjectImmediate(old);
                }

                var root = CreateChild(avatar.transform, RootObjectName, siblingIndex);

                var parameters = Undo.AddComponent<ModularAvatarParameters>(root);
                parameters.parameters = s.Parameters().Select(p => LocalParameter(p.name, p.type)).ToList();

                // menuToAppend / installTargetMenu は null のまま: 同じオブジェクトの Menu Item をアバターのルートメニューに入れる
                Undo.AddComponent<ModularAvatarMenuInstaller>(root);

                // ルートのサブメニュー。表示名は label（GameObject 名は検出用に固定）
                ConfigureSubMenu(Undo.AddComponent<ModularAvatarMenuItem>(root), s.menuLabel.Trim());

                AddToggle(root.transform, "Passthrough", s.enabledParameter, 1f);
                if (s.includeAuto) AddToggle(root.transform, "Auto", s.autoParameter, 1f);

                if (s.presets != null && s.presets.Count > 0)
                {
                    var presetMenu = CreateChild(root.transform, "Preset");
                    ConfigureSubMenu(Undo.AddComponent<ModularAvatarMenuItem>(presetMenu), "");
                    foreach (var preset in s.presets.OrderBy(p => p.index))
                    {
                        // Int パラメータに value = index の Toggle。選ぶとその番号になり、選択中を押し直すと 0 に戻る
                        AddToggle(presetMenu.transform, preset.label.Trim(), s.presetParameter, preset.index);
                    }
                }

                if (s.includeCalibrate) AddToggle(root.transform, "Calibrate", s.calibrateParameter, 1f);
                if (s.includeDebug) AddToggle(root.transform, "Debug", s.debugParameter, 1f);

                Undo.CollapseUndoOperations(undoGroup);
                MarkDirty(root);
                return root;
            }
            catch
            {
                // 途中で失敗したら作りかけを残さない
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        /// <summary>このツールが作ったオブジェクトを削除する（Undo 可）。</summary>
        internal static void Remove(VRCAvatarDescriptor avatar)
        {
            var existing = FindExisting(avatar);
            if (existing.Any(go => !CanRemove(go)))
            {
                throw new InvalidOperationException(
                    $"\"{RootObjectName}\" is part of a prefab. Open the prefab and delete it there (or unpack the prefab).");
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Remove Violin Passthrough OSC Menu");
            var undoGroup = Undo.GetCurrentGroup();

            foreach (var old in existing)
            {
                Undo.DestroyObjectImmediate(old);
            }

            Undo.CollapseUndoOperations(undoGroup);
            MarkDirty(avatar.gameObject);
        }

        private static ParameterConfig LocalParameter(string name, ParameterSyncType type)
        {
            // localOnly = 同期しない（Synced のチェックなし）。syncType は型の指定で、NotSynced にすると Expression Parameters に載らない
            return new ParameterConfig
            {
                nameOrPrefix = name,
                remapTo = "",
                internalParameter = false,
                isPrefix = false,
                syncType = type,
                localOnly = true,
                defaultValue = 0f,
                hasExplicitDefaultValue = true,
                saved = false,
            };
        }

        private static GameObject CreateChild(Transform parent, string name, int siblingIndex = -1)
        {
            var go = new GameObject(name);

            // Prefab Mode ではプレビュー用シーンに移してから親子付けする
            if (go.scene != parent.gameObject.scene)
            {
                EditorSceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
            }

            go.transform.SetParent(parent, false);
            if (siblingIndex >= 0) go.transform.SetSiblingIndex(siblingIndex);

            Undo.RegisterCreatedObjectUndo(go, UndoName);
            return go;
        }

        private static void AddToggle(Transform parent, string label, string parameter, float value)
        {
            var go = CreateChild(parent, label);
            var item = Undo.AddComponent<ModularAvatarMenuItem>(go);

            // 表示名は GameObject 名（label は空）。ヒエラルキーで名前を変えればメニュー名も変わる
            Configure(item, VRCExpressionsMenu.Control.ControlType.Toggle, label, parameter, value, "");
        }

        /// <summary>中身を子オブジェクトの Menu Item から作るサブメニュー（MenuSource = Children）。</summary>
        private static void ConfigureSubMenu(ModularAvatarMenuItem item, string label)
        {
            Configure(item, VRCExpressionsMenu.Control.ControlType.SubMenu,
                label.Length > 0 ? label : item.gameObject.name, "", 1f, label);
        }

        private static void Configure(
            ModularAvatarMenuItem item,
            VRCExpressionsMenu.Control.ControlType type,
            string controlName,
            string parameter,
            float value,
            string label)
        {
            item.Control = new VRCExpressionsMenu.Control
            {
                name = controlName,
                icon = null,
                type = type,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                value = value,
                subMenu = null,
                subParameters = Array.Empty<VRCExpressionsMenu.Control.Parameter>(),
                labels = Array.Empty<VRCExpressionsMenu.Control.Label>(),
            };
            item.MenuSource = SubmenuSource.Children;
            item.menuSource_otherObjectChildren = null;
            item.label = label;

            // 以下はパラメータが未宣言のとき MA が自動生成する場合の設定。MA Parameters で宣言済みだが同じ値にしておく
            item.isSynced = false;
            item.isSaved = false;
            item.isDefault = false;
            item.automaticValue = false;
        }

        private static void MarkDirty(GameObject go)
        {
            // Prefab Mode（プレビュー用シーン）では Undo の記録で Prefab ステージが dirty になる
            if (!EditorSceneManager.IsPreviewScene(go.scene))
            {
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
        }
    }
}
