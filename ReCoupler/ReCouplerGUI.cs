using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSP.UI.Screens;

namespace ReCoupler
{
    [KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
    public class ReCouplerGUI : MonoBehaviour
    {
        public static ReCouplerGUI Instance;

        public List<Part[]> partPairsToIgnore = new List<Part[]>();
        public List<Color> colorSpectrum = new List<Color> { Color.red, Color.yellow, Color.cyan, Color.blue, Color.magenta, Color.white };
        public bool GUIVisible = false;
        public bool highlightOn
        {
            get
            {
                return _highlightOn;
            }
            set
            {
                if (_highlightOn == value)
                    return;
                else
                    _highlightOn = value;
            }
        }
        
        private bool _highlightOn = false;
        private bool _highlightWasOn = false;
        private bool selectActive = false;
        private bool inputLocked = false;
        private const string iconPath = "ReCoupler/ReCoupler_Icon";
        private const string iconPath_off = "ReCoupler/ReCoupler_Icon_off";
        private const string iconPath_blizzy = "ReCoupler/ReCoupler_blizzy_Icon";
        private const string iconPath_blizzy_off = "ReCoupler/ReCoupler_blizzy_Icon_off";
        private string connectRadius_string = ReCouplerSettings.connectRadius_default.ToString();
        private string connectAngle_string = ReCouplerSettings.connectAngle_default.ToString();
        private bool allowRoboJoints_bool = ReCouplerSettings.allowRoboJoints_default;
        private bool allowKASJoints_bool = ReCouplerSettings.allowKASJoints_default;
        protected Rect ReCouplerWindow;
        private int guiId;
        internal protected List<AbstractJointTracker> jointsInvolved = null;
        public bool appLauncherEventSet = false;
        private List<Part> highlightedParts = new List<Part>();

        private static ApplicationLauncherButton button = null;
        internal static IButton blizzyToolbarButton = null;

        Logger log = new Logger("ReCouplerGui: ");

        public void Awake()
        {
            if (Instance)
                Destroy(Instance);
            Instance = this;

            if (!ActivateBlizzyToolBar())
            {
                //log.debug("Registering GameEvents.");
                appLauncherEventSet = true;
                GameEvents.onGUIApplicationLauncherReady.Add(OnGuiApplicationLauncherReady);
            }
            guiId = GUIUtility.GetControlID(FocusType.Passive);
            InputLockManager.RemoveControlLock("ReCoupler_EditorLock");
        }

        private void OnGuiApplicationLauncherReady()
        {
            button = ApplicationLauncher.Instance.AddModApplication(
                onTrue,
                onFalse,
                null,
                null,
                null,
                null,
                ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.FLIGHT,
                GameDatabase.Instance.GetTexture(iconPath, false));
        }

        internal bool ActivateBlizzyToolBar()
        {
            try
            {
                if (!ToolbarManager.ToolbarAvailable) return false;
                if (HighLogic.LoadedScene != GameScenes.EDITOR && HighLogic.LoadedScene != GameScenes.FLIGHT) return true;
                blizzyToolbarButton = ToolbarManager.Instance.add("ReCoupler", "ReCoupler");
                blizzyToolbarButton.TexturePath = iconPath_blizzy;
                blizzyToolbarButton.ToolTip = "ReCoupler";
                blizzyToolbarButton.Visible = true;
                blizzyToolbarButton.OnClick += (e) =>
                {
                    onButtonToggle();
                };
                return true;
            }
            catch
            {
                // Blizzy Toolbar instantiation error.  ignore.
                return false;
            }
        }

        public void onButtonToggle()
        {
            if (!GUIVisible)
                onTrue();
            else
                onFalse();
        }

        public void onTrue()
        {
            connectRadius_string = ReCouplerSettings.connectRadius.ToString();
            connectAngle_string = ReCouplerSettings.connectAngle.ToString();
            allowRoboJoints_bool = ReCouplerSettings.allowRoboJoints;
            allowKASJoints_bool = ReCouplerSettings.allowKASJoints;
        GUIVisible = true;
            if (button != null)
                button.SetTexture(GameDatabase.Instance.GetTexture(iconPath_off, false));
            if (blizzyToolbarButton != null)
                blizzyToolbarButton.TexturePath = iconPath_blizzy_off;
        }
        public void onFalse()
        {
            _highlightOn = false;
            selectActive = false;
            GUIVisible = false;
            UnlockEditor();
            if (button != null)
                button.SetTexture(GameDatabase.Instance.GetTexture(iconPath, false));
            if (blizzyToolbarButton != null)
                blizzyToolbarButton.TexturePath = iconPath_blizzy;
        }

        public void Start()
        {
            ReCouplerSettings.LoadSettings();
            this.connectRadius_string = ReCouplerSettings.connectRadius.ToString();
            this.connectAngle_string = ReCouplerSettings.connectAngle.ToString();
            this.allowRoboJoints_bool = ReCouplerSettings.allowRoboJoints;
            this.allowKASJoints_bool = ReCouplerSettings.allowKASJoints;
            if (!ReCouplerSettings.showGUI)
            {
                highlightOn = false;
                if (button != null)
                {
                    button.SetFalse(true);
                    ApplicationLauncher.Instance.RemoveModApplication(button);
                }
                if(appLauncherEventSet)
                    GameEvents.onGUIApplicationLauncherReady.Remove(OnGuiApplicationLauncherReady);
                if (blizzyToolbarButton != null)
                    blizzyToolbarButton.Destroy();
            }
        }

        public void OnDestroy()
        {
            //log.debug("Unregistering GameEvents.");
            if (appLauncherEventSet)
                GameEvents.onGUIApplicationLauncherReady.Remove(OnGuiApplicationLauncherReady);
            if (button != null)
                ApplicationLauncher.Instance.RemoveModApplication(button);
            if (blizzyToolbarButton != null)
                blizzyToolbarButton.Destroy();
            UnlockEditor();
        }

        public void OnGUI()
        {
            if (GUIVisible)
            {
                if (ReCouplerWindow.x == 0 && ReCouplerWindow.y == 0)
                {
                    ReCouplerWindow = new Rect(Screen.width * 2 / 3, Screen.height / 2, 300, 200);
                }
                ReCouplerWindow = GUILayout.Window(guiId, ReCouplerWindow, ReCouplerInterface, "ReCoupler", HighLogic.Skin.window, GUILayout.MinWidth(300), GUILayout.MinHeight(200));
            }
        }

        public void ReCouplerInterface(int GuiId)
        {
            GUIStyle standardButton = new GUIStyle(HighLogic.Skin.button);
            standardButton.padding = new RectOffset(8, 8, 8, 8);
            standardButton.normal.textColor = standardButton.focused.textColor = Color.white;
            standardButton.hover.textColor = standardButton.active.textColor = Color.white;

            GUIStyle disabledButton = new GUIStyle(HighLogic.Skin.button);
            disabledButton.padding = new RectOffset(8, 8, 8, 8);
            disabledButton.normal.textColor = disabledButton.focused.textColor = Color.gray;
            disabledButton.hover.textColor = disabledButton.active.textColor = Color.gray;

            GUIStyle textField = new GUIStyle(HighLogic.Skin.textField);

            GUILayout.BeginVertical(HighLogic.Skin.scrollView);
            _highlightOn = GUILayout.Toggle(_highlightOn, "Show ReCoupled Parts", standardButton);
            if (selectActive = GUILayout.Toggle(selectActive, "Remove a link", standardButton))
            {
                _highlightOn = true;
                LockEditor();
            }
            else
            {
                UnlockEditor();
            }
            if (GUILayout.Button("Reset links", standardButton))
            {
                partPairsToIgnore.Clear();
                if (HighLogic.LoadedSceneIsFlight && FlightReCoupler.Instance != null)
                {
                    FlightReCoupler.Instance.regenerateJoints(FlightGlobals.ActiveVessel);
                }
                else if (HighLogic.LoadedSceneIsEditor && EditorReCoupler.Instance != null)
                {
                    EditorReCoupler.Instance.ResetAndRebuild();
                }
            }
            GUILayout.Space(10);
            GUILayout.Label("Settings:");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Radius:", GUILayout.MinWidth(100));
            connectRadius_string = GUILayout.TextField(connectRadius_string, textField);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Angle:", GUILayout.MinWidth(100));
            connectAngle_string = GUILayout.TextField(connectAngle_string, textField);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Allow Robotic Nodes:", GUILayout.MinWidth(100));
            allowRoboJoints_bool = GUILayout.Toggle(allowRoboJoints_bool, allowRoboJoints_bool.ToString());
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Allow Cable Nodes:", GUILayout.MinWidth(100));
            allowKASJoints_bool = GUILayout.Toggle(allowKASJoints_bool, allowKASJoints_bool.ToString());
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Apply", standardButton))
            {
                float connectRadius_set, connectAngle_set;
                bool allowRoboJoints_set, allowKASJoints_set;
                if (float.TryParse(connectRadius_string, out connectRadius_set))
                    ReCouplerSettings.connectRadius = connectRadius_set;
                if (float.TryParse(connectAngle_string, out connectAngle_set))
                    ReCouplerSettings.connectAngle = connectAngle_set;
                if (bool.TryParse(allowRoboJoints_bool.ToString(), out allowRoboJoints_set))
                    ReCouplerSettings.allowRoboJoints = allowRoboJoints_set;
                if (bool.TryParse(allowKASJoints_bool.ToString(), out allowKASJoints_set))
                    ReCouplerSettings.allowKASJoints = allowKASJoints_set;
                if (HighLogic.LoadedSceneIsEditor && EditorReCoupler.Instance != null)
                    EditorReCoupler.Instance.ResetAndRebuild();
                else if (HighLogic.LoadedSceneIsFlight && FlightReCoupler.Instance != null)
                    FlightReCoupler.Instance.regenerateJoints(FlightGlobals.ActiveVessel);
            }
            GUILayout.EndVertical();
            
            GUIStyle exitButton = new GUIStyle(HighLogic.Skin.button);
            exitButton.normal.textColor = exitButton.focused.textColor = Color.red;
            exitButton.hover.textColor = exitButton.active.textColor = Color.red;
            if (GUI.Button(new Rect(ReCouplerWindow.width - 18, 2, 16, 16), "X", exitButton))
            {
                if (button != null)
                    button.SetFalse(true);
                else
                    onFalse();
            }
            GUI.DragWindow();  //new Rect(0, 0, 10000, 20)
        }

        public void highlightPart(Part part, int colorIndx = 0)
        {
            if (_highlightOn)
            {
                part.SetHighlightType(Part.HighlightType.AlwaysOn);
                part.SetHighlightColor(colorSpectrum[colorIndx % colorSpectrum.Count]);
                part.SetHighlight(true, false);
                if (!highlightedParts.Contains(part))
                    highlightedParts.Add(part);
            }
            else
            {
                part.SetHighlightDefault();
                highlightedParts.Remove(part);
            }
        }

        public void resetHighlighting(List<Part> parts)
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                parts[i].SetHighlightDefault();
                highlightedParts.Remove(parts[i]);
            }
        }

        public void Update()
        {
            if (_highlightOn || _highlightWasOn || selectActive)
            {
                if (HighLogic.LoadedSceneIsEditor && EditorReCoupler.Instance != null)
                    jointsInvolved = EditorReCoupler.Instance.hiddenNodes.CastList<AbstractJointTracker,EditorReCoupler.EditorJointTracker>();
                else if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null && FlightReCoupler.Instance != null)
                {
                    if (FlightReCoupler.Instance.trackedJoints.ContainsKey(FlightGlobals.ActiveVessel))
                        jointsInvolved = FlightReCoupler.Instance.trackedJoints[FlightGlobals.ActiveVessel].CastList<AbstractJointTracker,FlightReCoupler.FlightJointTracker>();
                    else
                    {
                        jointsInvolved = null;
                        log.debug("ActiveVessel is not in the dictionary!");
                    }
                }
                else
                {
                    jointsInvolved = null;
                    log.error("Could not get active joints!");
                    return;
                }
            }
            if (jointsInvolved == null)
            {
                resetHighlighting(highlightedParts);
                return;
            }
            if (highlightedParts.Count > 0)
                resetHighlighting(highlightedParts.FindAll((Part part) => jointsInvolved.All(jt => !jt.parts.Contains(part))));

            if (_highlightOn || _highlightWasOn)
            {
                for (int i = 0; i < jointsInvolved.Count; i++)
                {
                    for (int j = jointsInvolved[i].parts.Count - 1; j >= 0; j--)
                    {
                        highlightPart(jointsInvolved[i].parts[j], i);
                    }
                }
                _highlightWasOn = _highlightOn;
            }
            if (selectActive)
            {
                LockEditor();
                ScreenMessages.PostScreenMessage("Select a part in the ReCoupler joint for removal with ctrl + left mouseclick", Time.deltaTime, ScreenMessageStyle.UPPER_CENTER);
                if (Input.GetKeyUp(KeyCode.Mouse0) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
                {
                    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                    RaycastHit hit;

                    if (Physics.Raycast(ray, out hit))
                    {
                        Part hitPart = Part.FromGO(hit.transform.gameObject) ?? hit.transform.gameObject.GetComponentInParent<Part>();
                        if (hitPart == null)
                            hitPart = hit.collider.gameObject.GetComponentUpwards<Part>();
                        if (hitPart == null)
                            hitPart = SelectPartUnderMouse();

                        if (hitPart != null)
                        {
                            log.debug("Raycast hit part " + hitPart.name);
                            List<AbstractJointTracker> hitJoints = jointsInvolved.FindAll(j => j.parts.Contains(hitPart));
                            for (int i = hitJoints.Count - 1; i >= 0; i--)
                            {
                                log.debug("Destroying link between " + hitJoints[i].parts[0].name + " and " + hitJoints[i].parts[1].name);
                                hitJoints[i].Destroy();
                                partPairsToIgnore.Add(hitJoints[i].parts.ToArray());
                                if (HighLogic.LoadedSceneIsEditor && EditorReCoupler.Instance != null)
                                {
                                    EditorReCoupler.Instance.hiddenNodes.Remove((EditorReCoupler.EditorJointTracker)hitJoints[i]);
                                }
                                else if (HighLogic.LoadedSceneIsFlight && FlightReCoupler.Instance != null && FlightReCoupler.Instance.trackedJoints.ContainsKey(FlightGlobals.ActiveVessel))
                                {
                                    FlightReCoupler.Instance.trackedJoints[FlightGlobals.ActiveVessel].Remove((FlightReCoupler.FlightJointTracker)hitJoints[i]);
                                }
                                hitJoints[i].parts[0].SetHighlightDefault();
                                hitJoints[i].parts[1].SetHighlightDefault();
                            }

                            UnlockEditor();
                            selectActive = false;
                        }
                        else
                            log.debug("Hit part was null: ");
                    }
                }
            }
        }

        private void LockEditor()
        {
            if (inputLocked)
                return;
            if (!HighLogic.LoadedSceneIsEditor)
                return;
            inputLocked = true;
            //EditorLogic.fetch.Lock(false, false, false, "ReCoupler_EditorLock");
            InputLockManager.SetControlLock(ControlTypes.EDITOR_SOFT_LOCK, "ReCoupler_EditorLock");
            log.debug("Locking editor");
        }

        private void UnlockEditor()
        {
            if (!inputLocked)
                return;
            //EditorLogic.fetch.Unlock("ReCoupler_EditorLock");
            InputLockManager.RemoveControlLock("ReCoupler_EditorLock");
            inputLocked = false;
            log.debug("Unlocking editor");
        }

        public Part SelectPartUnderMouse()
        {
            log.debug("Using failsafe part select method.");
            FlightCamera CamTest = new FlightCamera();
            CamTest = FlightCamera.fetch;
            Ray ray = CamTest.mainCamera.ScreenPointToRay(Input.mousePosition);
            LayerMask RayMask = new LayerMask();
            RayMask = 1 << 0;
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, Mathf.Infinity, RayMask))
            {

                return FlightGlobals.ActiveVessel.Parts.Find(p => p.gameObject == hit.transform.gameObject);
                //The critical bit. Note I'm generating a list of possible objects hit and then asking if I hit one of them. I'm not starting with the object hit and trying to work my way up.
            }
            return null;
        }
    }
}
