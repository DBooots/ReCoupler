using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KSP.UI.Screens;
using UnityEngine;

namespace ReCoupler
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class EditorReCoupler : MonoBehaviour
    {
        public static EditorReCoupler Instance;

        Logger log = new Logger("EditorReCoupler: ");
        
        public List<AttachNode> openNodes = new List<AttachNode>();
        public List<EditorJointTracker> hiddenNodes = new List<EditorJointTracker>();

        public float connectRadius = ReCouplerSettings.connectRadius_default;
        public float connectAngle = ReCouplerSettings.connectAngle_default;
        
        public class EditorJointTracker
        {
            public List<AttachNode> nodes;
            public List<Part> parts;

            private List<bool> structNodeMan = new List<bool> { false, false };

            public EditorJointTracker(IList<AttachNode> nodes)
            {
                this.nodes = nodes.ToList();
                this.parts = new List<Part> { nodes[0].owner, nodes[1].owner };
            }

            public void Create()
            {
                nodes[0].attachedPart = parts[1];
                nodes[1].attachedPart = parts[0];
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }

            public void Destroy()
            {
                //UnsetModuleStructuralNode(nodes[0], structNodeMan[0]);
                //UnsetModuleStructuralNode(nodes[1], structNodeMan[1]);
                nodes[0].attachedPart = null;
                nodes[1].attachedPart = null;
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }

            private static bool SetModuleStructuralNode(AttachNode node)
            {
                bool structNodeMan = false;
                ModuleStructuralNode structuralNode = node.owner.FindModulesImplementing<ModuleStructuralNode>().FirstOrDefault(msn => msn.attachNodeNames.Equals(node.id));
                if (structuralNode != null)
                {
                    structNodeMan = structuralNode.spawnManually;
                    structuralNode.spawnManually = true;
                    structuralNode.SpawnStructure();
                }
                return structNodeMan;
            }

            private static void UnsetModuleStructuralNode(AttachNode node, bool structNodeMan)
            {
                ModuleStructuralNode structuralNode = node.owner.FindModulesImplementing<ModuleStructuralNode>().FirstOrDefault(msn => msn.attachNodeNames.Equals(node.id));
                if (structuralNode != null)
                {
                    structuralNode.DespawnStructure();
                    structuralNode.spawnManually = structNodeMan;
                }
            }
        }
        
        void Awake()
        {
            if (Instance)
                Destroy(Instance);
            Instance = this;
        }

        void Start()
        {
            ReCouplerSettings.LoadSettings(out connectRadius, out connectAngle);

            log.debug("Registering GameEvents.");
            GameEvents.onEditorPartEvent.Add(OnEditorPartEvent);
            GameEvents.onEditorRedo.Add(OnEditorUnRedo);
            GameEvents.onEditorUndo.Add(OnEditorUnRedo);
            GameEvents.onEditorLoad.Add(OnEditorLoad);
            GameEvents.onEditorRestart.Add(OnEditorRestart);
            GameEvents.onLevelWasLoaded.Add(OnLevelWasLoaded);

            GameEvents.onGameSceneSwitchRequested.Add(OnGameSceneSwitchRequested);

            reConstruct();
            //GameEvents.onEditorShipModified.Add(OnEditorShipModified);
        }

        public void OnDestroy()
        {
            log.debug("Unregistering GameEvents.");
            GameEvents.onEditorPartEvent.Remove(OnEditorPartEvent);
            GameEvents.onEditorRedo.Remove(OnEditorUnRedo);
            GameEvents.onEditorUndo.Remove(OnEditorUnRedo);
            GameEvents.onEditorLoad.Remove(OnEditorLoad);
            GameEvents.onEditorRestart.Remove(OnEditorRestart);
            GameEvents.onLevelWasLoaded.Remove(OnLevelWasLoaded);

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

        private void OnLevelWasLoaded(GameScenes data)
        {
            if (HighLogic.LoadedSceneIsEditor)
                reConstruct();
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

        /*public void OnEditorShipModified(ShipConstruct ship)
        {
            reConstruct(ship);
        }*/

        public void generateFromShipConstruct(ShipConstruct ship)
        {
            openNodes = new List<AttachNode>();
            //foreach(Part part in ship.Parts)
            for (int i = 0; i < ship.Parts.Count; i++)
            {
                //checkPartNodes(part);
                List<AttachNode> problemNodes = ReCouplerManager.findProblemNodes(ship.Parts[i]);
                for (int j = problemNodes.Count - 1; j >= 0; j--)
                    if (!hiddenNodes.Any((EditorJointTracker jt) => jt.nodes.Contains(problemNodes[j])))
                        problemNodes[j].attachedPart = null;

                checkPartNodes(ship.Parts[i]);
            }
        }

        public void checkPartNodes(Part part, bool recursive = false)
        {
            if (part == null)
            {
                log.error("checkPartNodes(part): part is null!");
                return;
            }

            List<AttachNode> partNodes = ReCouplerManager.findOpenNodes(part);

            if (recursive)
            {
                Part[] children = part.FindChildParts<Part>(true);
                partNodes.AddRange(ReCouplerManager.findOpenNodes(children));
            }

            List<AttachNode> partNodesToAdd = new List<AttachNode>();

            //foreach (AttachNode node in partNodes)
            for (int i = 0; i < partNodes.Count; i++)
            {
                bool doNotJoin = false;
                foreach (ModuleCargoBay cargoBay in part.FindModulesImplementing<ModuleCargoBay>())
                {
                    if (cargoBay.nodeInnerForeID == partNodes[i].id || cargoBay.nodeInnerAftID == partNodes[i].id)
                    {
                        doNotJoin = true;
                        break;
                    }
                }
                if (doNotJoin)
                    continue;

                AttachNode closestNode = ReCouplerManager.getEligiblePairing(partNodes[i], openNodes, connectRadius, connectAngle);
                if (closestNode != null)
                {
                    EditorJointTracker newJointTracker = new EditorJointTracker(new List<AttachNode> { partNodes[i], closestNode });
                    hiddenNodes.Add(newJointTracker);
                    newJointTracker.Create();
                    openNodes.Remove(closestNode);

                    ConnectedLivingSpacesCompatibility.RequestAddConnection(newJointTracker.parts[0], newJointTracker.parts[1]);

                    //ReCouplerManager.combineCrossfeedSets(closestNode.owner, partNodes[i].owner);
                }
                else
                    partNodesToAdd.Add(partNodes[i]);
            }
            openNodes.AddRange(partNodesToAdd);
        }

        public void reConstruct(ShipConstruct ship)
        {
            //log.debug("reConstruct(ship)");
            
            for (int i = hiddenNodes.Count - 1; i >= 0; i--)
            {
                ConnectedLivingSpacesCompatibility.RequestRemoveConnection(hiddenNodes[i].parts[0], hiddenNodes[i].parts[1]);
                hiddenNodes[i].Destroy();
            }
            hiddenNodes.Clear();
            openNodes.Clear();

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
                    List<AttachNode> problemNodes = ReCouplerManager.findProblemNodes(children[i]);
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
    }
}
