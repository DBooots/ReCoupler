using System;
using System.Collections.Generic;
using System.Linq;
using KSP.UI.Screens;
using UnityEngine;

namespace ReCoupler
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class EditorReCoupler : MonoBehaviour
    {
        public static EditorReCoupler Instance;

        Logger log = new Logger("ReCoupler: EditorReCoupler: ");
        
        public List<AttachNode> openNodes = new List<AttachNode>();
        public List<EditorJointTracker> hiddenNodes = new List<EditorJointTracker>();
        
        void Awake()
        {
            if (Instance)
                Destroy(Instance);
            Instance = this;
        }

        void Start()
        {
            ReCouplerSettings.LoadSettings();
            
            GameEvents.onEditorPartEvent.Add(OnEditorPartEvent);
            GameEvents.onEditorRedo.Add(OnEditorUnRedo);
            GameEvents.onEditorUndo.Add(OnEditorUnRedo);
            GameEvents.onEditorLoad.Add(OnEditorLoad);
            GameEvents.onEditorRestart.Add(OnEditorRestart);

            GameEvents.onGameSceneSwitchRequested.Add(OnGameSceneSwitchRequested);

            ResetAndRebuild(forbidAdding: true);
            //GameEvents.onEditorShipModified.Add(OnEditorShipModified);
        }

        public void OnDestroy()
        {
            GameEvents.onEditorPartEvent.Remove(OnEditorPartEvent);
            GameEvents.onEditorRedo.Remove(OnEditorUnRedo);
            GameEvents.onEditorUndo.Remove(OnEditorUnRedo);
            GameEvents.onEditorLoad.Remove(OnEditorLoad);
            GameEvents.onEditorRestart.Remove(OnEditorRestart);

            GameEvents.onGameSceneSwitchRequested.Remove(OnGameSceneSwitchRequested);

            //GameEvents.onEditorShipModified.Remove(OnEditorShipModified);
        }

        private void OnGameSceneSwitchRequested(GameEvents.FromToAction<GameScenes, GameScenes> FromToData)
        {
            if (FromToData.from == GameScenes.EDITOR)
            {
                for (int i = hiddenNodes.Count - 1; i >= 0; i--)
                {
                    hiddenNodes[i].Destroy();
                }
                hiddenNodes.Clear();
            }
        }

        private void OnEditorRestart()
        {
            ResetAndRebuild(forbidAdding: true);
        }

        public void OnEditorLoad(ShipConstruct ship, CraftBrowserDialog.LoadType type)
        {
            if (type == CraftBrowserDialog.LoadType.Normal)
            {
                ResetAndRebuild(forbidAdding: true);
            }
        }

        public void OnEditorUnRedo(ShipConstruct ship)
        {
            ResetAndRebuild(forbidAdding: true);
        }

        public void generateFromShipConstruct(ShipConstruct ship, bool forbidAdding = false)
        {
            openNodes.Clear();
            generateJoints(ship);

            foreach (EditorJointTracker joint in ReCouplerUtils.Generate_Editor(ship, openNodes))
            {
                if (forbidAdding)
                {
                    ReCouplerGUI.Instance.partPairsToIgnore.Add(joint.parts.ToArray());
                    joint.Destroy();
                }
                else
                    hiddenNodes.Add(joint);
            }
        }

        public void generateJoints(ShipConstruct vessel)
        {
            List<Part> vesselParts = vessel.parts;

            List<Part> childen;
            Part activePart;
            for (int p = 0; p < vesselParts.Count; p++)
            {
                activePart = vesselParts[p];
                childen = activePart.FindChildParts<Part>(false).ToList();
                if (activePart.parent != null)
                    childen.Add(activePart.parent);
                for (int n = 0; n < activePart.attachNodes.Count; n++)
                {
                    if (activePart.attachNodes[n].attachedPart != null && !childen.Contains(activePart.attachNodes[n].attachedPart))
                    {
                        parseAttachNodes(activePart.attachNodes[n], activePart.attachNodes[n].attachedPart.attachNodes.FindAll(AN => AN.attachedPart == activePart));
                    }
                }
            }
        }

        private void parseAttachNodes(AttachNode parentNode, List<AttachNode> childNodes)
        {
            for (int i = 0; i < childNodes.Count; i++)
            {
                EditorJointTracker existingTracker = hiddenNodes.FirstOrDefault((EditorJointTracker jt) => jt.nodes.Contains(parentNode) && jt.nodes.Contains(childNodes[i]));
                if (existingTracker == null)
                    hiddenNodes.Add(new EditorJointTracker(parentNode, childNodes[i]));
            }
        }

        public void ResetAndRebuild(ShipConstruct ship = null, bool forbidAdding = false)
        {
            if (ship == null)
                ship = EditorLogic.fetch.ship;
            //log.debug("reConstruct(ship)");
            
            for (int i = hiddenNodes.Count - 1; i >= 0; i--)
            {
                hiddenNodes[i].Destroy();
            }
            hiddenNodes.Clear();
            //openNodes.Clear();

            if (ship == null)
            {
                log.warning("ShipConstruct is null.");
                return;
            }

            generateFromShipConstruct(ship, forbidAdding);
        }

        /*public void OnEditorPartDeleted(Part part)
        {
            reConstruct();
        }*/

        public void OnEditorPartEvent(ConstructionEventType constEvent, Part part)
        {
            List<Part> partsInvolved = new List<Part> { part };
            partsInvolved.AddRange(part.FindChildParts<Part>(true).ToList());
            /*foreach (Part p in part.symmetryCounterparts)
            {
                partsInvolved.Add(p);
                partsInvolved.AddRange(p.FindChildParts<Part>(true).ToList());
            }*/

            if (constEvent == ConstructionEventType.PartAttached ||
                constEvent == ConstructionEventType.PartOffset ||
                constEvent == ConstructionEventType.PartRotated)
            {
                /*for (int p = 0; p < partsInvolved.Count; p++)
                {
                    foreach (EditorJointTracker jt in ReCouplerUtils.checkPartNodes(partsInvolved[p], openNodes, ReCouplerUtils.JointType.EditorJointTracker))
                    {
                        hiddenNodes.Add(jt);
                    }
                }*/

                ResetAndRebuild();
            }
            else if (constEvent == ConstructionEventType.PartDetached)
            {
                for (int i = ReCouplerGUI.Instance.partPairsToIgnore.Count - 1; i >= 0; i--)
                {
                    for (int j = ReCouplerGUI.Instance.partPairsToIgnore[i].Length - 1; j >= 0; j--)
                    {
                        if (!EditorLogic.fetch.ship.parts.Contains(ReCouplerGUI.Instance.partPairsToIgnore[i][j]))
                        {
                            ReCouplerGUI.Instance.partPairsToIgnore.RemoveAt(i);
                            break;
                        }
                    }
                }
                ResetAndRebuild();
            }
            /*else if (constEvent == ConstructionEventType.PartDetached)
            {
                List<EditorJointTracker> jointsInvolved;
                for (int p = 0; p < partsInvolved.Count; p++)
                {
                    jointsInvolved = hiddenNodes.FindAll(jt => jt.parts.Contains(partsInvolved[p]));
                    for (int i = jointsInvolved.Count - 1; i >= 0; i--)
                    {
                        jointsInvolved[i].Destroy();
                        hiddenNodes.Remove(jointsInvolved[i]);
                    }
                }
            }
            else if (constEvent == ConstructionEventType.PartOffset || constEvent == ConstructionEventType.PartRotated)
            {
                List<EditorJointTracker> jointsInvolved;
                for (int p = 0; p < partsInvolved.Count; p++)
                {
                    jointsInvolved = hiddenNodes.FindAll(jt => jt.parts.Contains(partsInvolved[p]));
                    for (int i = jointsInvolved.Count - 1; i >= 0; i--)
                    {
                        jointsInvolved[i].Destroy();
                        hiddenNodes.Remove(jointsInvolved[i]);
                    }
                }

                for (int p = 0; p < partsInvolved.Count; p++)
                {
                    foreach (EditorJointTracker jt in ReCouplerUtils.checkPartNodes(partsInvolved[p], openNodes, ReCouplerUtils.JointType.EditorJointTracker))
                    {
                        hiddenNodes.Add(jt);
                    }
                }
            }*/
            else if (constEvent == ConstructionEventType.PartCopied)
            {
                for (int i = partsInvolved.Count - 1; i >= 0; i--)
                {
                    List<AttachNode> problemNodes = ReCouplerUtils.findProblemNodes(partsInvolved[i]);
                    for (int j = problemNodes.Count - 1; j >= 0; j--)
                        problemNodes[j].attachedPart = null;
                }
            }
            /*else if (constEvent == ConstructionEventType.PartDeleted)
            {
                List<EditorJointTracker> jointsInvolved;
                for (int p = 0; p < partsInvolved.Count; p++)
                {
                    jointsInvolved = hiddenNodes.FindAll(jt => jt.parts.Contains(partsInvolved[p]));
                    for (int i = jointsInvolved.Count - 1; i >= 0; i--)
                    {
                        jointsInvolved[i].Destroy();
                        hiddenNodes.Remove(jointsInvolved[i]);
                    }
                }
            }*/
        }

        public class EditorJointTracker : AbstractJointTracker
        {
            public EditorJointTracker(AttachNode parentNode, AttachNode childNode) : base(parentNode, childNode)
            {
                this.SetNodes();
                ConnectedLivingSpacesCompatibility.RequestAddConnection(parts[0], parts[1]);
                ReCouplerUtils.onReCouplerEditorJointFormed.Fire(new GameEvents.FromToAction<Part, Part>(this.parts[0], this.parts[1]));
            }

            public EditorJointTracker(AbstractJointTracker parent) : base(parent.nodes[0], parent.nodes[1])
            {
                this.SetNodes();
                ConnectedLivingSpacesCompatibility.RequestAddConnection(parts[0], parts[1]);
                ReCouplerUtils.onReCouplerEditorJointFormed.Fire(new GameEvents.FromToAction<Part, Part>(this.parts[0], this.parts[1]));
            }

            public override void SetNodes()
            {
                base.SetNodes();
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }

            public override void Destroy()
            {
                //UnsetModuleStructuralNode(nodes[0], structNodeMan[0]);
                //UnsetModuleStructuralNode(nodes[1], structNodeMan[1]);
                base.Destroy();
                ConnectedLivingSpacesCompatibility.RequestRemoveConnection(parts[0], parts[1]);
                ReCouplerUtils.onReCouplerEditorJointBroken.Fire(new GameEvents.FromToAction<Part, Part>(this.parts[0], this.parts[1]));
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
        }
    }
}
