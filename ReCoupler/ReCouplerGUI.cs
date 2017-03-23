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
        private EditorReCoupler editorInstance;
        public List<Color> colorSpectrum = new List<Color> { Color.red, Color.yellow, Color.cyan, Color.blue, Color.magenta, Color.white };
        private bool highlightOn = false;
        private bool highlightWasOn = false;
        private bool selectActive = false;
        private const string iconPath = "ReCoupler/Recoupler_Icon";

        public ApplicationLauncherButton button = null;

        Logger log = new Logger("ReCouplerGui: ");

        public void Awake()
        {
            if (Instance)
                Destroy(Instance);
            Instance = this;

            GameEvents.onGUIApplicationLauncherReady.Add(OnGuiApplicationLauncherReady);
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
            highlightOn = true;
            selectActive = true;
        }
        public void onFalse()
        {
            highlightOn = false;
            selectActive = false;
        }

        public void Start()
        {
            ReCouplerSettings.LoadSettings();

            //log.debug("Registering GameEvents.");
        }

        public void OnDestroy()
        {
            //log.debug("Unregistering GameEvents.");
            GameEvents.onGUIApplicationLauncherReady.Remove(OnGuiApplicationLauncherReady);
            ApplicationLauncher.Instance.RemoveModApplication(button);
        }

        public void OnGUI()
        {
        }

        public void highlightPart(Part part, int colorIndx = 0)
        {
            if (highlightOn)
            {
                part.SetHighlightType(Part.HighlightType.AlwaysOn);
                part.SetHighlightColor(colorSpectrum[colorIndx % colorSpectrum.Count]);
                part.SetHighlight(true, false);
            }
            else if (highlightWasOn)
            {
                part.SetHighlightDefault();
            }
        }

        public void Update()
        {
            if (highlightOn || highlightWasOn)
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
                highlightWasOn = highlightOn;
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
                            hitPart = SelectPartUnderMouse();
                        //Part hitPart = (Part)UIPartActionController.GetComponentUpwards("Part", hit.collider.gameObject);
                        if (hitPart == null)
                            log.debug("Hit part was null: ");
                        else
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
                    }
                }
            }
        }

        public Part SelectPartUnderMouse()
        {
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
