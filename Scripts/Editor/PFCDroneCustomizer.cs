using PFCTools.Utils;
using PFCTools2.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using Utils.EditorExtension;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace PFCTools.Drone {
    [InitializeOnLoad]
    public class PFCDroneCustomizer : EditorWindow {

        public PFCDroneInstaller customizer = null;
        public static async Task<EditorWindow> OpenEditor(PFCDroneInstaller targetCustomizer) {
            PFCDroneCustomizer window = EditorWindow.GetWindow(typeof(PFCDroneCustomizer), false, "PFCDroneCustomizer") as PFCDroneCustomizer;
            window.minSize = new Vector2(400, 400);
            window.customizer = targetCustomizer;
            await window.customizer.versionManager.GetLatestPackageVersion();
            return window;
        }

        private void OnGUI() {
            GUIStyle label = new GUIStyle(EditorStyles.wordWrappedLabel);
            label.richText = true;
            if (customizer == null) {
                GUILayout.Label("No drone was selected or the drone was removed. Please use the customizer to open the editor window.");
                GUILayout.Label($"No customizer Found On Selected Object: {Selection.activeGameObject}", label);
                if (Selection.activeGameObject != null) {
                    customizer = Selection.activeGameObject.GetComponent<PFCDroneInstaller>();
                    this.Repaint();
                }
            } else if (customizer.gameObject == null) {
                return;
            } else if (customizer.descriptor == null) {
                GUILayout.Label("No Avatar Descriptor found, Please make sure the prefab is parented to your model.\n" +
                    "(it doesn't matter where. the script will ensure everything is in the correct place by the end.\n" +
                    "it just needs to be somewhere on the model you want to intall it on.)\n\n" +
                    "Please drag the prefab anywhere onto the model you want to install it on and then click back on this window to continue.", label);
            } else {

                GUILayout.BeginHorizontal(EditorStyles.toolbar);

                if (GUILayout.Button("Restart Setup", EditorStyles.toolbarButton)) {
                    customizer.currentCustomizerWindow = CustomizerWindows.ModeSelect;
                    customizer.selectedMountPoint = (int)AdvancedMountPoints.SelectAMountPoint;
                }

                GUILayout.FlexibleSpace();
                customizer.visualizeMarkers = GUILayout.Toggle(customizer.visualizeMarkers, new GUIContent("", EditorGUIUtility.IconContent("d_scenevis_visible_hover").image, "Toggle All Visualization"), EditorStyles.toolbarButton, GUILayout.MaxWidth(30));
                customizer.AutoCam = GUILayout.Toggle(customizer.AutoCam, new GUIContent("", EditorGUIUtility.IconContent("Camera Icon").image, "Enable Automatic Camera Movement"), EditorStyles.toolbarButton, GUILayout.MaxWidth(30));

                if (GUILayout.Button("Remove Drone", EditorStyles.toolbarButton)) {
                    if (EditorUtility.DisplayDialog("Remove Drone", "Are you sure you want to remove the drone from your avatar?", "Delete it all!", "Cancel")) {
                        Selection.activeObject = null;
                        customizer.removeAllDroneComponents();
                        if (EditorUtility.DisplayDialog("Animation layers removal", "We'll now show the animator installation step once more so you can use it to remove the non-prefab parts of your model, hitting finish on the installer at this point will result in the window closing", "Ok")) {
                            customizer.currentCustomizerWindow = CustomizerWindows.InstallAnimators;
                        }
                    }
                }
                GUILayout.EndHorizontal();

                //Installer Steps

                //Mode Select
                if (customizer.currentCustomizerWindow == CustomizerWindows.ModeSelect) {
                    PageHeader(new string[] { "Mode Select", "1/3" });

                    GUILayout.Label("Welcome to the <b>PFC Drone Kit</b> Installation Wizard\n" +
                        "Would you like to run the installer in Simple mode or Advanced mode?\n", label);
                    GUILayout.BeginHorizontal();
                    GUILayout.BeginVertical();
                    if (GUILayout.Button("Simple Mode")) {
                        customizer.advancedMode = false;
                        customizer.currentCustomizerWindow = CustomizerWindows.Attach;

                    }
                    GUILayout.Label("<b>Simple mode</b>: \nFor people with little to no unity knowledge, Will handle most of the process for you.\n", label);
                    GUILayout.EndVertical();

                    GUILayout.BeginVertical();
                    if (GUILayout.Button("Advanced Mode")) {
                        customizer.advancedMode = true;
                        customizer.currentCustomizerWindow = CustomizerWindows.Attach;
                    }
                    GUILayout.Label("<b>Advanced Mode</b> : \nOffers extended installtion options like manual layer importing and custom attachment points.", label);
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();

                }

                //Attachment
                if (customizer.currentCustomizerWindow == CustomizerWindows.Attach) {
                    PageHeader(new string[] { "Select attachment point", "2/3" });
                    GUILayout.Label("First pick a location where you want to install the drone.\n" +
                        "Depending on the mode you'll have more options", label);
                    EditorGUI.BeginChangeCheck();
                    if (customizer.advancedMode) {
                        AdvancedMountPoints selection = (AdvancedMountPoints)customizer.selectedMountPoint;
                        selection = (AdvancedMountPoints)EditorGUILayout.EnumPopup("Select mount point", selection);
                        customizer.selectedMountPoint = (int)selection;

                        if (customizer.selectedMountPoint == (int)AdvancedMountPoints.Custom) {
                            customizer.customMountPoint = (Transform)EditorGUILayout.ObjectField(customizer.customMountPoint, typeof(Transform), true);
                        }
                        EditorGUI.BeginDisabledGroup(!(Enum.IsDefined(typeof(AdvancedMountPoints), selection) && selection != AdvancedMountPoints.Custom || (selection == AdvancedMountPoints.Custom && customizer.customMountPoint != null)));
                        ConfirmMountPoint();
                        EditorGUI.EndDisabledGroup();
                    } else //if simple mode
                      {
                        SimpleMountPoints selection = (SimpleMountPoints)customizer.selectedMountPoint;
                        selection = (SimpleMountPoints)EditorGUILayout.EnumPopup("Left or Right handed?", selection);
                        customizer.selectedMountPoint = (int)selection;
                        EditorGUI.BeginDisabledGroup(!(Enum.IsDefined(typeof(SimpleMountPoints), selection)));
                        ConfirmMountPoint();
                        EditorGUI.EndDisabledGroup();
                    }
                    if (EditorGUI.EndChangeCheck()) {
                        customizer.performStepAttach();
                    }
                }

                //Install Animator Files

                if (customizer.currentCustomizerWindow == CustomizerWindows.InstallAnimators) {
                    customizer.UpdateExpressionMenus();
                    PageHeader(new string[] { "Merge animator data", "3/3" });

                    #region WDCheck
                    if (customizer.FXWDState.HasFlag(AnimatorWDState.Mixed)) {
                        string assumption = "Enabled";
                        if (customizer.FXWDState.HasFlag(AnimatorWDState.Off)) {
                            assumption = "Disabled";
                        }

                        if (customizer.advancedMode) {
                            GUILayout.Label($"<color=red><b>WARNING:</b></color> Write Default States are <b>Mixed</b>. If this is intended feel free to ignore this.", label);
                        } else {
                            GUILayout.Label($"<color=red><b>WARNING:</b></color> Write Default States are <b>Mixed</b>.\nBased on the animator we assumed it's meant to be <Color=cyan>{assumption}</color>\nIf this is not the case, please check your animator or use the auto fix below.\n If you know what you're doing feel free to ignore this.", label);
                        }

                        PFCGUI.HorizontalLine();
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Autofix Write Defaults:");
                        if (GUILayout.Button("Force WD On")) {
                            WDConverter.SetWriteDefaults(customizer.GetFXLayer(), true);
                            WDConverter.SetWriteDefaults(customizer.droneAnimationController, true);
                            customizer.DroneWDState = AnimatorWDState.On;
                            customizer.FXWDState = AnimatorWDState.On;
                        }
                        if (GUILayout.Button("Force WD Off")) {
                            WDConverter.SetWriteDefaults(customizer.GetFXLayer(), false);
                            WDConverter.SetWriteDefaults(customizer.droneAnimationController, false);
                            customizer.DroneWDState = AnimatorWDState.Off;
                            customizer.FXWDState = AnimatorWDState.Off;
                        }
                        if (GUILayout.Button(new GUIContent(" Refresh", EditorGUIUtility.IconContent("d_Refresh").image, "Refresh WD Status"), GUILayout.Width(75))) {
                            customizer.FXWDState = AnimatorHelper.GetWDState(customizer.GetFXLayer());
                        }

                        GUILayout.EndHorizontal();
                    }
                    #endregion
                    #region advanced mode
                    if (customizer.advancedMode) {
                        EasyImportButton(label, false);
                        #region WD Toggle
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"The drone's Write Defaults are currently set to:", label);
                        if (GUILayout.Button((customizer.DroneWDState.HasFlag(AnimatorWDState.On) ? "Enabled" : "Disabled"))) {
                            if (customizer.DroneWDState.HasFlag(AnimatorWDState.On)) {
                                WDConverter.SetWriteDefaults(customizer.droneAnimationController, false);
                                customizer.DroneWDState = AnimatorWDState.Off;
                            } else {
                                WDConverter.SetWriteDefaults(customizer.droneAnimationController, true);
                                customizer.DroneWDState = AnimatorWDState.On;
                            }
                        }
                        GUILayout.EndHorizontal();
                        PFCGUI.HorizontalLine();
                        #endregion

                        #region Buttons
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label("Open Animator Layer Tools");
                        if (GUILayout.Button("Open")) {
                            AnimatorLayerCopy.ShowWindow();
                            AnimatorLayerCopy.AnimatorControllerSource = customizer.droneAnimationController;
                            if (customizer.descriptor.baseAnimationLayers[4].animatorController != null) {
                                AnimatorLayerCopy.AnimatorControllerTarget = (AnimatorController)customizer.descriptor.baseAnimationLayers[4].animatorController;
                            }

                            AnimatorLayerCopy.ExpressionSource = customizer.droneExpParameters;
                            if (customizer.descriptor.expressionParameters != null) {
                                AnimatorLayerCopy.ExpressionTarget = customizer.descriptor.expressionParameters;
                            }

                            AnimatorLayerCopy.MenuSource = customizer.droneExpMenu;
                            if (customizer.descriptor.expressionsMenu != null) {
                                AnimatorLayerCopy.MenuTarget = customizer.descriptor.expressionsMenu;
                            }

                            AnimatorLayerCopy.ShowAnimatorMergeTools.target = true;
                            AnimatorLayerCopy.ShowLayerMatches = true;
                            AnimatorLayerCopy.ShowExpressionMergeTools.target = true;
                            AnimatorLayerCopy.ShowMenuMergeTools.target = true;
                        }
                        EditorGUILayout.EndHorizontal();
                        #endregion
                    }
                    #endregion
                    #region simple mode
                    else {
                        EasyImportButton(label, true);

                        if (!customizer.ExpressionMenus.ContainsKey(customizer.droneExpMenu)) {
                            PFCGUI.HorizontalLine();
                            GUILayout.Label("<b>Import Menu</b>:\nPlease select which sub menu you'd like to have your drone menu placed in.", label);
                            foreach (KeyValuePair<VRCExpressionsMenu, ExpressionMenuData> kvp in customizer.ExpressionMenus) {
                                VRCExpressionsMenu subMenu = kvp.Key;
                                string name = kvp.Value.Name;
                                if (subMenu.controls.Count + customizer.droneExpMenu.controls.Count > 8) GUI.enabled = false;
                                if (GUILayout.Button(name)) {

                                    VRCExpressionsMenu.Control newControl = new VRCExpressionsMenu.Control();
                                    newControl.subMenu = customizer.droneExpMenu;
                                    newControl.name = customizer.droneExpName;
                                    newControl.icon = customizer.droneExpIcon;
                                    newControl.type = VRCExpressionsMenu.Control.ControlType.SubMenu;
                                    subMenu.controls.Add(newControl);
                                    EditorUtility.SetDirty(subMenu);
                                    AssetDatabase.SaveAssets();
                                    customizer.UpdateExpressionMenus();
                                    break;
                                }
                                GUI.enabled = true;
                            }
                        } else {
                            PFCGUI.HorizontalLine();
                            //EditorGUILayout.BeginHorizontal();
                            string menuName = "Main Menu";
                            if (customizer.ExpressionMenus.ContainsKey(customizer.droneExpMenu)) {
                                ExpressionMenuData menu = customizer.ExpressionMenus[customizer.droneExpMenu];
                                if (menu.Parent != null) {
                                    menuName = menu.Parent.Name;
                                }
                            }
                            GUILayout.Label($"<b>Menu is installed:{menuName}</b>", label);
                            if (customizer.descriptor.expressionsMenu == customizer.droneExpMenu) GUI.enabled = false;
                            if (GUILayout.Button("remove")) {

                                VRCExpressionsMenu parent = customizer.ExpressionMenus[customizer.droneExpMenu].Parent.Menu;
                                foreach (VRCExpressionsMenu.Control control in parent.controls) {
                                    if (control.subMenu == customizer.droneExpMenu) {
                                        parent.controls.Remove(control);
                                        EditorUtility.SetDirty(parent);
                                        AssetDatabase.SaveAssets();
                                        break;
                                    }
                                }
                                customizer.UpdateExpressionMenus();
                            }
                            GUI.enabled = true;
                            //EditorGUILayout.EndHorizontal();
                        }
                    }
                    #endregion

                    if (customizer.droneExpParameters != null) {
                        PFCGUI.HorizontalLine();
                        GUILayout.Label("<b>Import Expression Paramters</b>\nNext up is the expression paramters.", label);
                        if (customizer.descriptor.expressionParameters == customizer.droneExpParameters) {
                            if (GUILayout.Button("Clear Parameters")) {
                                customizer.descriptor.expressionParameters = null;
                            }
                        } else if (customizer.descriptor.expressionParameters == null) {
                            if (GUILayout.Button("Add Parameters")) {
                                customizer.descriptor.expressionParameters = customizer.droneExpParameters;
                            }

                        } else {
                            bool HasParameters = false;
                            foreach (VRCExpressionParameters.Parameter DroneParam in customizer.droneExpParameters.parameters) {
                                foreach (VRCExpressionParameters.Parameter AvatarParam in customizer.descriptor.expressionParameters.parameters) {
                                    if (DroneParam.name == AvatarParam.name) {
                                        HasParameters = true;
                                        break;
                                    }
                                }
                            }

                            if (HasParameters) {
                                if (GUILayout.Button("Remove Parameters")) {
                                    List<VRCExpressionParameters.Parameter> DescParamList = new List<VRCExpressionParameters.Parameter>(customizer.descriptor.expressionParameters.parameters);
                                    foreach (VRCExpressionParameters.Parameter parameter in customizer.droneExpParameters.parameters) {
                                        foreach (VRCExpressionParameters.Parameter descparam in DescParamList) {
                                            if (parameter.name == descparam.name) {
                                                DescParamList.Remove(descparam);
                                                break;
                                            }
                                        }
                                    }
                                    customizer.descriptor.expressionParameters.parameters = DescParamList.ToArray();
                                    EditorUtility.SetDirty(customizer.descriptor.expressionParameters);
                                    AssetDatabase.SaveAssets();
                                }
                            } else {
                                if (customizer.droneExpParameters.CalcTotalCost() < VRCExpressionParameters.MAX_PARAMETER_COST - customizer.descriptor.expressionParameters.CalcTotalCost()) {
                                    if (GUILayout.Button("Add Parameters")) {
                                        List<VRCExpressionParameters.Parameter> DescParamList = new List<VRCExpressionParameters.Parameter>(customizer.descriptor.expressionParameters.parameters);

                                        foreach (VRCExpressionParameters.Parameter param in customizer.droneExpParameters.parameters) {

                                            VRCExpressionParameters.Parameter newParam = new VRCExpressionParameters.Parameter();

                                            ClassCopier.Copy<VRCExpressionParameters.Parameter>(param, newParam);
                                            DescParamList.Add(newParam);


                                        }

                                        customizer.descriptor.expressionParameters.parameters = DescParamList.ToArray();
                                        EditorUtility.SetDirty(customizer.descriptor.expressionParameters);
                                        AssetDatabase.SaveAssets();

                                    }
                                } else {
                                    GUI.enabled = false;
                                    GUILayout.Button("Parameter Limit Exceeded");
                                    GUI.enabled = true;
                                }
                            }
                        }
                    }

                    PFCGUI.HorizontalLine();
                    if (customizer.Prefab != null) {
                        GUILayout.Label("<b>Final Step</b>:\nif you're done here or you already have the layers imported from a previous install you can just click the button below to finalize the installation and go to the customizer.", label);
                        if (GUILayout.Button("Finish")) {

                            customizer.currentCustomizerWindow = CustomizerWindows.Customize;
                            customizer.name = "PFCDroneKit Customizer [" + customizer.Prefab.transform.parent.name + "]";
                        }
                    } else {
                        GUILayout.Label("<b>Close window</b>:\nOnce everything is removed you can close the menu.", label);
                        if (GUILayout.Button("Close")) {
                            DestroyImmediate(customizer.gameObject);
                            Close();
                            return;
                        }
                    }
                }

                //Customizer
                if (customizer.currentCustomizerWindow == CustomizerWindows.Customize) {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Select:");
                    if (GUILayout.Button("Drone", EditorStyles.miniButtonLeft)) {
                        Selection.activeGameObject = null;
                        if (customizer.AutoCam) {
                            SceneView.lastActiveSceneView.LookAt(customizer.DroneModel.transform.position, Quaternion.Euler(20, 180, 0), .2f);
                        }
                    }
                    if (GUILayout.Button("Dock", EditorStyles.miniButtonMid)) {
                        Selection.activeGameObject = customizer.DockOffset;
                        if (customizer.AutoCam) {
                            SceneView.lastActiveSceneView.LookAt(Selection.activeGameObject.transform.position, Quaternion.Euler(20, 180, 0), .5f);
                        }
                    }
                    if (GUILayout.Button("Hud", EditorStyles.miniButtonMid)) {
                        Selection.activeGameObject = customizer.HudLink;
                        if (customizer.AutoCam) {
                            SceneView.lastActiveSceneView.LookAt(Selection.activeGameObject.transform.position, Quaternion.Euler(20, 45, 0), 1);
                        }
                    }
                    if (GUILayout.Button("Follow Point", EditorStyles.miniButtonMid)) {
                        Selection.activeGameObject = customizer.FollowPoint;
                        if (customizer.AutoCam) {
                            SceneView.lastActiveSceneView.LookAt(Selection.activeGameObject.transform.position, Quaternion.Euler(45, 90, 0), .8f);
                        }
                    }
                    if (GUILayout.Button("Selfie Point", EditorStyles.miniButtonMid)) {
                        Selection.activeGameObject = customizer.SelfiePoint;
                        if (customizer.AutoCam) {
                            SceneView.lastActiveSceneView.LookAt(Selection.activeGameObject.transform.position, Quaternion.Euler(20, 90, 0), 2f);
                        }
                    }
                    if (GUILayout.Button("Minimap Cutoff", EditorStyles.miniButtonRight)) {
                        Selection.activeGameObject = customizer.MapCutoff;
                        if (customizer.AutoCam) {
                            SceneView.lastActiveSceneView.LookAt(Selection.activeGameObject.transform.position, Quaternion.Euler(20, 90, 0), 2f);
                        }
                    }
                    GUILayout.EndHorizontal();
                    //customizer.visualizeMarkers = GUILayout.Toggle(customizer.visualizeMarkers, "Show all markers");
                    PFCGUI.HorizontalLine();
                    GUILayout.Space(2);

                    if (Selection.activeGameObject == customizer.HudLink) {
                        if (customizer.hudHasEmission()) {
                            customizer.HudEmissionColor = EditorGUILayout.ColorField("Hud Emission Color", customizer.HudEmissionColor);
                        }
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Hud Curvature");
                        Vector3 pos = customizer.HudLink.transform.localScale;
                        pos.z = EditorGUILayout.Slider(customizer.HudLink.transform.localScale.z, 0, (8 * customizer.HudLink.transform.localScale.x));
                        customizer.HudLink.transform.localScale = pos;
                        GUILayout.EndHorizontal();
                        if (GUILayout.Button("Recalculate Hud Position")) {
                            customizer.RecalculateHudPosition();
                        }
                        if (GUILayout.Button("Rotate Hud To Face Viewpoint")) {
                            customizer.RecalculateHudRotation();
                        }
                    } else if (Selection.activeGameObject == customizer.DockOffset) { //IF dock is being edited
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label("Flip Screen Orientation");
                        if (GUILayout.Button("Left Handed")) {
                            customizer.ScreenRotator.transform.localRotation = Quaternion.Euler(90, 0, 0);
                        }
                        if (GUILayout.Button("Right Handed")) {
                            customizer.ScreenRotator.transform.localRotation = Quaternion.Euler(-90, 0, 180);
                        }
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PrefixLabel("Front strap width");
                        customizer.DockRenderer.SetBlendShapeWeight(0, EditorGUILayout.Slider(customizer.DockRenderer.GetBlendShapeWeight(0), 0, 100));
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PrefixLabel("Front strap height");
                        customizer.DockRenderer.SetBlendShapeWeight(1, EditorGUILayout.Slider(customizer.DockRenderer.GetBlendShapeWeight(1), 0, 100));
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PrefixLabel("Back strap width");
                        customizer.DockRenderer.SetBlendShapeWeight(2, EditorGUILayout.Slider(customizer.DockRenderer.GetBlendShapeWeight(2), 0, 100));
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PrefixLabel("Back strap height");
                        customizer.DockRenderer.SetBlendShapeWeight(3, EditorGUILayout.Slider(customizer.DockRenderer.GetBlendShapeWeight(3), 0, 100));
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PrefixLabel("Hide Straps");
                        customizer.DockRenderer.SetBlendShapeWeight(4, EditorGUILayout.Slider(customizer.DockRenderer.GetBlendShapeWeight(4), 0, 100));
                        EditorGUILayout.EndHorizontal();
                    } else if (Selection.activeGameObject == customizer.FollowPoint) {//if followpoint is being edited.
                        if (GUILayout.Button("Reset FollowPoint Position")) {
                            customizer.RecalculateFollowPoint();
                        }
                    } else if (Selection.activeGameObject == customizer.SelfiePoint) {//If Selfiepoint is being edited.
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Selfie Distance");
                        Vector3 pos = customizer.SelfiePoint.transform.position;
                        pos.z = EditorGUILayout.Slider(customizer.SelfiePoint.transform.position.z, 0, 5);
                        customizer.SelfiePoint.transform.position = pos;
                        GUILayout.EndHorizontal();

                        EditorGUILayout.LabelField("Selfie Target:");
                        GUILayout.BeginHorizontal();
                        PositionConstraint SelfieConstraint = customizer.SelfiePoint.GetComponent<PositionConstraint>();
                        ConstraintSource TargetSource = SelfieConstraint.GetSource(0);
                        if (GUILayout.Button("Head")) {
                            TargetSource.sourceTransform = customizer.HeadLink.transform;
                        }
                        if (GUILayout.Button("Hip")) {
                            TargetSource.sourceTransform = customizer.animator.GetBoneTransform(HumanBodyBones.Hips);
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Custom: ");

                        TargetSource.sourceTransform = (Transform)EditorGUILayout.ObjectField(TargetSource.sourceTransform, typeof(Transform), true);
                        SelfieConstraint.SetSource(0, TargetSource);
                        GUILayout.EndHorizontal();

                    } else if (Selection.activeGameObject == customizer.MapCutoff) {//If Selfiepoint is being edited.
                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Minimap Cutoff Height");
                        Vector3 pos = customizer.MapCutoff.transform.position;
                        pos.y = EditorGUILayout.FloatField(customizer.MapCutoff.transform.position.y);
                        customizer.MapCutoff.transform.position = pos;
                        EditorGUILayout.LabelField("m");
                        GUILayout.EndHorizontal();
                        if (GUILayout.Button("Recalculate Camera Cutoff")) {
                            customizer.FixMinimapOffsets();
                        }
                    } else { //IF drone is being edited
                        if (customizer.droneHasEmission()) {
                            customizer.droneEmissionColor = EditorGUILayout.ColorField("Drone Emission Color", customizer.droneEmissionColor);
                        }
                        //GUILayout.BeginHorizontal();
                        //EditorGUILayout.LabelField("Tilt Reduction");
                        //customizer.TiltReduction = EditorGUILayout.FloatField(customizer.TiltReduction);
                        //GUILayout.EndHorizontal();
                    }
                    //PFCGUI.HorizontalLine();
                }

                //Footer
                GUILayout.BeginVertical();
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal(EditorStyles.toolbar);

                GUIStyle HighlightButton = new GUIStyle(EditorStyles.toolbarButton);
                HighlightButton.richText = true;

                GUILayout.Label($"Version: {customizer.versionManager.GetVersion()}", EditorStyles.toolbarButton);
                if (customizer.versionManager.GetLatestVersion() != null) {
                    if (customizer.versionManager.GetVersion() < customizer.versionManager.GetLatestVersion()) GUILayout.Label($"Latest Release: <Color=cyan>{customizer.versionManager.GetLatestVersion()}</color>", HighlightButton);
                    if (customizer.versionManager.GetVersion() > customizer.versionManager.GetLatestVersion()) GUILayout.Label($"Latest Release: <Color=yellow>{customizer.versionManager.GetLatestVersion()}</color>", HighlightButton);
                    if ((customizer.versionManager.packageUrl != "" && customizer.versionManager.GetVersion() < customizer.versionManager.GetLatestVersion()) && GUILayout.Button("<color=cyan>Install</color>", HighlightButton)) {
                        //Application.OpenURL(PFCDroneInstaller.downloadURL);
                        WebClient webclient = new WebClient();
                        if (!AssetDatabase.IsValidFolder("Assets/Updates")) {
                            AssetDatabase.CreateFolder("Assets", "Updates");
                        }
                        webclient.DownloadFile(customizer.versionManager.packageUrl, "Assets/Updates/PFCDroneKitUpdate.unitypackage");
                        AssetDatabase.Refresh();
                        AssetDatabase.ImportPackage("Assets/Updates/PFCDroneKitUpdate.unitypackage", true);
                    }
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Join my Discord!", EditorStyles.toolbarButton)) {
                    Application.OpenURL("https://discord.gg/FJKB768");
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
        }

        private void EasyImportButton(GUIStyle label, bool simpleMode) {
            if (simpleMode) {
                GUILayout.Label("<b>Importing Animator Data:</b>\n" + "Most of this step is automated for ease of use in Simple mode. Simply click the button below to import all animation layers.", label);
            } else {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("<b>Import Animator Layers:</b>", label);
            }


            AnimatorController FXLayer = customizer.GetFXLayer();

            if (FXLayer == null) {
                GUILayout.Label("No FX layer found, so we'll be making a copy of the drone layer and inserting that instead.");
                if (GUILayout.Button("Insert")) {
                    string FilePath = AssetDatabase.GetAssetPath(customizer.droneAnimationController);
                    string BasePath = Path.GetDirectoryName(FilePath);
                    string FileName = Path.GetFileNameWithoutExtension(FilePath);
                    string Extention = Path.GetExtension(FilePath);
                    string newPath = $"{BasePath}/{FileName}_copy/{Extention}";
                    AssetDatabase.CopyAsset(FilePath, newPath);
                    AnimatorController newAnimator = AssetDatabase.LoadAssetAtPath<AnimatorController>(newPath);

                    for (int i = 0; i < customizer.descriptor.baseAnimationLayers.Length; i++) {
                        VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.CustomAnimLayer layer = customizer.descriptor.baseAnimationLayers[i];
                        if (layer.type == VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX) {
                            layer.animatorController = newAnimator;
                        }
                    }
                }
            } else if (AnimatorLayerCopy.LayersExistIn(customizer.droneAnimationController, FXLayer)) {
                if (FXLayer != customizer.droneAnimationController) {
                    if (GUILayout.Button("Remove Layers")) {
                        customizer.removeDroneAnimatorLayers();
                    }
                } else {
                    if (GUILayout.Button("Remove Animator")) {
                        for (int i = 0; i < customizer.descriptor.baseAnimationLayers.Length; i++) {
                            VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.CustomAnimLayer layer = customizer.descriptor.baseAnimationLayers[i];
                            if (layer.type == VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX) {
                                layer.animatorController = null;
                            }
                        }
                    }
                }
            } else {
                if (GUILayout.Button("Import")) {
                    if (simpleMode) {
                        AnimatorWDState WDState = AnimatorHelper.GetWDState(customizer.GetFXLayer());
                        if (WDState.HasFlag(AnimatorWDState.Mixed)) WDConverter.SetWriteDefaults(customizer.droneAnimationController, true);
                        else if (WDState.HasFlag(AnimatorWDState.On)) WDConverter.SetWriteDefaults(customizer.droneAnimationController, true);
                        else if (WDState.HasFlag(AnimatorWDState.Off)) WDConverter.SetWriteDefaults(customizer.droneAnimationController, false);
                    }

                    AnimatorLayerCopy.AnimatorControllerSource = customizer.droneAnimationController;
                    AnimatorLayerCopy.AnimatorControllerTarget = FXLayer;

                    Dictionary<string, AnimatorControllerLayer> LayerDifferences = new Dictionary<string, AnimatorControllerLayer>();
                    foreach (AnimatorControllerLayer layer in customizer.droneAnimationController.layers) {
                        LayerDifferences.Add(layer.name, layer);
                    }
                    foreach (AnimatorControllerLayer layer in FXLayer.layers) {
                        if (LayerDifferences.ContainsKey(layer.name)) {
                            LayerDifferences.Remove(layer.name);
                        }
                    }
                    AnimatorLayerCopy.CopyAllLayers(LayerDifferences);

                    Dictionary<string, AnimatorControllerParameterType> ParameterDifferences = new Dictionary<string, AnimatorControllerParameterType>();
                    foreach (AnimatorControllerParameter param in customizer.droneAnimationController.parameters) {
                        ParameterDifferences.Add(param.name, param.type);
                    }
                    foreach (AnimatorControllerParameter param in FXLayer.parameters) {
                        if (ParameterDifferences.ContainsKey(param.name)) {
                            ParameterDifferences.Remove(param.name);
                        }
                    }
                    AnimatorLayerCopy.CopyAllParameters(ParameterDifferences);
                };
            }
            if (!simpleMode) EditorGUILayout.EndHorizontal();
        }

        private void PageHeader(string[] entries) {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.FlexibleSpace();
            foreach (string entry in entries) {
                GUILayout.Label($"{entry}", EditorStyles.toolbarButton);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void ConfirmMountPoint() {
            if (GUILayout.Button("Confirm mount point.")) {
                customizer.currentCustomizerWindow = CustomizerWindows.InstallAnimators;
                AnimatorWDState FXWDState = AnimatorHelper.GetWDState(customizer.GetFXLayer());
                AnimatorWDState DroneWDState = AnimatorHelper.GetWDState(customizer.droneAnimationController);
                customizer.DroneWDState = DroneWDState;
                customizer.FXWDState = FXWDState;
                customizer.UpdateExpressionMenus();
            }
        }
    }
}