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

            reConstruct();
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
            reConstruct();
        }

        public void OnEditorLoad(ShipConstruct ship, CraftBrowserDialog.LoadType type)
        {
            if (type == CraftBrowserDialog.LoadType.Normal)
            {
                reConstruct();
            }
        }

        public void generateFromShipConstruct(ShipConstruct ship)
        {
            openNodes.Clear();
            //foreach(Part part in ship.Parts)
            for (int i = 0; i < ship.Parts.Count; i++)
            {
                //checkPartNodes(part);
                List<AttachNode> problemNodes = ReCouplerUtils.findProblemNodes(ship.Parts[i]);
                for (int j = problemNodes.Count - 1; j >= 0; j--)
                    if (!hiddenNodes.Any((EditorJointTracker jt) => jt.nodes.Contains(problemNodes[j])))
                        problemNodes[j].attachedPart = null;

                //checkPartNodes(ship.Parts[i]);
            }
            foreach (EditorJointTracker joint in ReCouplerUtils.Generate_Editor(ship, openNodes))
            {
                hiddenNodes.Add(joint);
            }
        }

        public void reConstruct(ShipConstruct ship)
        {
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

            generateFromShipConstruct(ship);
        }

        public void reConstruct()
        {
            //log.debug("reConstruct()");
            reConstruct(EditorLogic.fetch.ship);
        }

        public void OnEditorUnRedo(ShipConstruct ship)
        {
            reConstruct(ship);
        }

        /*public void OnEditorPartDeleted(Part part)
        {
            reConstruct();
        }*/

        public void OnEditorPartEvent(ConstructionEventType constEvent, Part part)
        {
            if (constEvent == ConstructionEventType.PartAttached || constEvent == ConstructionEventType.PartDetached ||
                constEvent == ConstructionEventType.PartOffset || constEvent == ConstructionEventType.PartRotated)
            {
                /*for (int i = problematicNodes.Count - 1; i >= 0; i--)
                {
                    log.debug("Resetting node on " + problematicNodes[i].owner.name + ". " + constEvent);
                    problematicNodes[i].attachedPart = null;
                    problematicNodes.RemoveAt(i);
                }*/

                reConstruct();
            }
            else if (constEvent == ConstructionEventType.PartCopied)
            {
                List<Part> children = new List<Part> { part };
                children.AddRange(part.FindChildParts<Part>(true));
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    List<AttachNode> problemNodes = ReCouplerUtils.findProblemNodes(children[i]);
                    for (int j = problemNodes.Count - 1; j >= 0; j--)
                        problemNodes[j].attachedPart = null;
                }
            }

            /*if (constEvent == ConstructionEventType.PartAttached)
            {
                log.debug("Part Attached");
                if (part == null)
                {
                    log.error("Attached part is null?!");
                    return;
                }

                if (part.parent != null && part.parent.FindAttachNodeByPart(part) != null)
                    openNodes.Remove(part.parent.FindAttachNodeByPart(part));

                checkPartNodes(part, true);

            } else if (constEvent == ConstructionEventType.PartDetached)
            {
                log.debug("Part Detached.");
                List<AttachNode> partNodes = part.attachNodes;
                Part[] children = part.FindChildParts<Part>(true);
                for(int i = 0; i < children.Length; i++)
                {
                    partNodes.AddRange(children[i].attachNodes);
                }
                
                foreach(AttachNode node in part.attachNodes)
                {
                    if (nodePairs.ContainsKey(node))
                    {
                        showNode(node);
                        showNode(nodePairs[node]);
                        nodePairs.Remove(nodePairs[node]);
                        nodePairs.Remove(node);
                    }
                    openNodes.Remove(node);
                    if (hiddenNodes.ContainsKey(node))
                        showNode(node);
                }
            }*/
            /*else if (constEvent == ConstructionEventType.PartCopied)
            {
                log.debug("Copied a part.");
                List<Part> partsCopied = new List<Part>();
                partsCopied.Add(part);
                partsCopied.AddRange(part.FindChildParts<Part>(true));
                foreach(Part copiedPart in partsCopied)
                {
                    foreach(ModuleAttachNodeHide hidingModule in copiedPart.FindModulesImplementing<ModuleAttachNodeHide>())
                    {
                        log.debug("Copied part had a ModuleAttachNodeHide: " + copiedPart.name);
                        hidingModule.wasDuplicated();
                        hidingModule.show();
                        copiedPart.RemoveModule(hidingModule);
                    }
                }
                reConstruct();
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
