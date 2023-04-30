using UnityEditor;
using UnityEngine;
namespace PFCTools.Drone {
    [CustomEditor(typeof(PFCDroneInstaller))]
    public class PFCDroneInstallerEditor : Editor {

        private PFCDroneInstaller installer;


        private void OnEnable() {
            installer = (PFCDroneInstaller)target;
        }
        public override void OnInspectorGUI() {

            string buttonText = "";



            if (installer.currentCustomizerWindow == CustomizerWindows.PreInstall) {
                if (installer.transform.parent == null) {
                    GUILayout.TextArea("No Avatar Descriptor found, Please make sure the prefab is parented to your model.\n" +
                    "(it doesn't matter where. the script will ensure everything is in the correct place by the end.\n" +
                    "it just needs to be somewhere on the model you want to intall it on.)\n\n" +
                    "Please drag the prefab anywhere onto the model you want to install it on and then click back on this window to continue.");
                } else {
                    GUILayout.TextArea("The PFCDronePro Installer is designed to make installation of the PFCDronekitPro as easy as possible. \nTo begin the installation just press the button below.");
                    buttonText = "Open Drone Installer";
                }
            } else if (installer.currentCustomizerWindow != CustomizerWindows.Customize) {
                buttonText = "Open Installer";
            } else {
                buttonText = "Open Customizer";
            }

            if (buttonText != "") {
                if (GUILayout.Button(buttonText)) {
                    if (installer.currentCustomizerWindow == CustomizerWindows.PreInstall) {
                        installer.currentCustomizerWindow = CustomizerWindows.ModeSelect;
                    }

                    _ = PFCDroneCustomizer.OpenEditor(installer);
                }
            }
        }
    }
}