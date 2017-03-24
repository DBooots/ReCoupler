using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.UI.Screens;

namespace ReCoupler
{
    [KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
    class ReCouplerGUI : MonoBehaviour
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

        private EditorReCoupler editorInstance;
        private bool _highlightOn = false;
        private bool _highlightWasOn = false;
        private bool selectActive = false;
        private const string iconPath = "ReCoupler/Recoupler_Icon";
        private const string iconPath_off = "ReCoupler/Recoupler_Icon_off";
        private string connectRadius_string = ReCouplerSettings.connectRadius_default.ToString();
        private string connectAngle_string = ReCouplerSettings.connectAngle_default.ToString();
        protected Rect ReCouplerWindow;
        private int guiId;
        public bool appLauncherEventSet = false;

        public ApplicationLauncherButton button = null;

        Logger log = new Logger("ReCouplerGui: ");

        public void Awake()
        {
            if (Instance)
                Destroy(Instance);
            Instance = this;

            //log.debug("Registering GameEvents.");
            appLauncherEventSet = true;
            GameEvents.onGUIApplicationLauncherReady.Add(OnGuiApplicationLauncherReady);
            guiId = GUIUtility.GetControlID(FocusType.Passive);
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

        public void onTrue()
        {
            connectRadius_string = ReCouplerSettings.connectRadius.ToString();
            connectAngle_string = ReCouplerSettings.connectAngle.ToString();
            GUIVisible = true;
            button.SetTexture(GameDatabase.Instance.GetTexture(iconPath_off, false));
        }
        public void onFalse()
        {
            _highlightOn = false;
            selectActive = false;
            GUIVisible = false;
            button.SetTexture(GameDatabase.Instance.GetTexture(iconPath, false));
        }

        public void Start()
        {
            ReCouplerSettings.LoadSettings();
            this.connectRadius_string = ReCouplerSettings.connectRadius.ToString();
            this.connectAngle_string = ReCouplerSettings.connectAngle.ToString();
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
            }
        }

        public void OnDestroy()
        {
            //log.debug("Unregistering GameEvents.");
            if (appLauncherEventSet)
                GameEvents.onGUIApplicationLauncherReady.Remove(OnGuiApplicationLauncherReady);
            if (button != null)
                ApplicationLauncher.Instance.RemoveModApplication(button);
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
            if (selectActive = GUILayout.Toggle(selectActive, "Remove a link", HighLogic.LoadedSceneIsFlight ? standardButton : disabledButton))
            {
                if (!HighLogic.LoadedSceneIsFlight)
                    selectActive = false;
                else
                    _highlightOn = true;
            }
            if (GUILayout.Button("Reset links", HighLogic.LoadedSceneIsFlight ? standardButton : disabledButton))
            {
                partPairsToIgnore.Clear();
                if (HighLogic.LoadedSceneIsFlight)
                {
                    ReCouplerManager manager = null;
                    if (FlightGlobals.ActiveVessel != null)
                        manager = FlightGlobals.ActiveVessel.vesselModules.FirstOrDefault(vModule => vModule is ReCouplerManager) as ReCouplerManager;
                    if (manager != null)
                    {
                        manager.generateJoints();
                    }
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

            if (GUILayout.Button("Apply", standardButton))
            {
                float connectRadius_set, connectAngle_set;
                ReCouplerManager manager = null;
                if (FlightGlobals.ActiveVessel != null)
                    manager = FlightGlobals.ActiveVessel.vesselModules.FirstOrDefault(vModule => vModule is ReCouplerManager) as ReCouplerManager;
                if (float.TryParse(connectRadius_string, out connectRadius_set))
                {
                    ReCouplerSettings.connectRadius = connectRadius_set;
                    if (manager != null)
                        manager.connectRadius = connectRadius_set;
                }
                if (float.TryParse(connectAngle_string, out connectAngle_set))
                {
                    ReCouplerSettings.connectAngle = connectAngle_set;
                    if (manager != null)
                        manager.connectAngle = connectAngle_set;
                }
            }
            GUILayout.EndVertical();
            
            GUIStyle exitButton = new GUIStyle(HighLogic.Skin.button);
            exitButton.normal.textColor = exitButton.focused.textColor = Color.red;
            exitButton.hover.textColor = exitButton.active.textColor = Color.red;
            if (GUI.Button(new Rect(ReCouplerWindow.width - 18, 2, 16, 16), "X", exitButton))
            {
                button.SetFalse(true);
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
            }
            else if (_highlightWasOn)
            {
                part.SetHighlightDefault();
            }
        }

        public void Update()
        {
            if (_highlightOn || _highlightWasOn)
            {
                if (HighLogic.LoadedSceneIsEditor && EditorReCoupler.Instance != null)
                {
                    editorInstance = EditorReCoupler.Instance;
                    for (int i = 0; i < editorInstance.nodePairs.Count; i++)
                    {
                        for (int j = editorInstance.nodePairs[i].Length - 1; j >= 0; j--)
                        {
                            highlightPart(editorInstance.nodePairs[i][j].owner, i);
                        }
                    }
                }
                else if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null)
                {
                    ReCouplerManager manager = FlightGlobals.ActiveVessel.vesselModules.FirstOrDefault(vModule => vModule is ReCouplerManager) as ReCouplerManager;
                    if (manager == null)
                    {
                        log.error("Couldn't find ReCouplerManager VesselModule!");
                        return;
                    }

                    for (int i = 0; i < manager.joints.Count; i++)
                    {
                        for (int j = manager.joints[i].parts.Count - 1; j >= 0; j--)
                        {
                            highlightPart(manager.joints[i].parts[j], i);
                        }
                    }
                }
                _highlightWasOn = _highlightOn;
            }
            if (selectActive)
            {
                if (Input.GetKeyUp(KeyCode.Mouse0))
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
                            if (HighLogic.LoadedSceneIsEditor && EditorReCoupler.Instance != null)
                            {
                                editorInstance = EditorReCoupler.Instance;
                                List<Part[]> joints = new List<Part[]>();
                                List<AttachNode[]> attNodes = editorInstance.nodePairs.FindAll((AttachNode[] np) => np[0].owner == hitPart || np[1].owner == hitPart);
                                for (int i = attNodes.Count - 1; i >= 0; i--)
                                {
                                    editorInstance.showNode(attNodes[i][0]);
                                    editorInstance.showNode(attNodes[i][1]);
                                    partPairsToIgnore.Add(new Part[] { attNodes[i][0].owner, attNodes[i][1].owner });
                                }
                            }
                            else if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null)
                            {
                                ReCouplerManager manager = FlightGlobals.ActiveVessel.vesselModules.FirstOrDefault(vModule => vModule is ReCouplerManager) as ReCouplerManager;
                                if (manager == null)
                                {
                                    log.error("Couldn't find ReCouplerManager VesselModule!");
                                    return;
                                }

                                List<ReCouplerManager.JointTracker> joints = manager.joints.FindAll(jt => jt.parts.Contains(hitPart));
                                for (int i = joints.Count - 1; i >= 0; i--)
                                {
                                    log.debug("Destroying link between " + joints[i].parts[0].name + " and " + joints[i].parts[1].name);
                                    joints[i].destroyLink();
                                    foreach (ModuleDecouple decoupler in joints[i].decouplers)
                                        manager.decouplersInvolved.Remove(decoupler);
                                    if (joints[i].oldCrossfeedSets.Count > 0)
                                    {
                                        joints[i].parts[0].crossfeedPartSet = joints[i].oldCrossfeedSets[0];
                                        joints[i].parts[1].crossfeedPartSet = joints[i].oldCrossfeedSets[1];
                                    }
                                    joints[i].parts[0].SetHighlightDefault();
                                    joints[i].parts[1].SetHighlightDefault();
                                    manager.joints.Remove(joints[i]);
                                    partPairsToIgnore.Add(new Part[] { joints[i].parts[0], joints[i].parts[1] });
                                }
                            }
                            selectActive = false;
                        }
                        else
                            log.debug("Hit part was null: ");
                    }
                }
            }
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
