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

        public Dictionary<AttachNode, ModuleAttachNodeHide> hidingModules = new Dictionary<AttachNode, ModuleAttachNodeHide>();
        public Dictionary<AttachNode, AttachNode> nodePairs = new Dictionary<AttachNode, AttachNode>();
        public List<AttachNode> openNodes = new List<AttachNode>();

        public float connectRadius = ReCouplerManager.connectRadius_default;
        public float connectAngle = ReCouplerManager.connectAngle_default;

        void Awake()
        {
            if (Instance)
                Destroy(Instance);
            Instance = this;
        }

        void Start()
        {
            ReCouplerManager.LoadSettings(out connectRadius, out connectAngle);

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
            showAllNodes();
        }

        private void OnGameSceneSwitchRequested(GameEvents.FromToAction<GameScenes, GameScenes> FromToData)
        {
            showAllNodes();
            if (FromToData.from == GameScenes.EDITOR)
            {
                for (int i = hidingModules.Count - 1; i >= 0; i--)
                {
                    var keyValuePair = hidingModules.ElementAt(i);
                    keyValuePair.Key.owner.RemoveModule(keyValuePair.Value);
                    hidingModules.Remove(keyValuePair.Key);
                }
                hidingModules.Clear();
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
                    nodePairs.Add(partNodes[i], closestNode);
                    nodePairs.Add(closestNode, partNodes[i]);
                    hideNode(partNodes[i]);
                    hideNode(closestNode);
                    openNodes.Remove(closestNode);

                    ConnectedLivingSpacesCompatibility.RequestAddConnection(partNodes[i].owner, closestNode.owner);

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
            log.debug("Removing existing extra PartModules");
            for (int i = hidingModules.Count - 1; i >= 0; i--)
            {
                var keyValuePair = hidingModules.ElementAt(i);
                keyValuePair.Key.owner.RemoveModule(keyValuePair.Value);
                hidingModules.Remove(keyValuePair.Key);
            }

            showAllNodes();
            hidingModules.Clear();
            for(int i = nodePairs.Count - 1; i>=0; i--)
            {
                ConnectedLivingSpacesCompatibility.RequestRemoveConnection(nodePairs.Keys.ElementAt(i).owner, nodePairs.Values.ElementAt(i).owner);
            }
            nodePairs.Clear();
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
                reConstruct();

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
            else if (constEvent == ConstructionEventType.PartCopied)
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
            }
        }

        public void showAllNodes()
        {
            for(int i = hidingModules.Count-1; i>=0; i--)
            {
                ModuleAttachNodeHide node = hidingModules.Values.ElementAt(i);
                if (node != null)
                    node.show();
                else
                {
                    log.error("hidingModules element was null!");
                    hidingModules.Remove(hidingModules.Keys.ElementAt(i));
                }
            }
        }

        public void hideNode(AttachNode node)
        {
            if (!hidingModules.ContainsKey(node))
            {
                log.debug("Creating PartModule on " + node.owner.name);
                ModuleAttachNodeHide hidingModule = createModule(node);
                if (hidingModule != null)
                    hidingModules.Add(node, hidingModule);
            }
            if (hidingModules.ContainsKey(node))
            {
                log.debug("Hiding node on " + node.owner.name);
                hidingModules[node].hide();
            }
        }

        ModuleAttachNodeHide createModule(AttachNode node)
        {
            ModuleAttachNodeHide hideModule = node.owner.AddModule("ModuleAttachNodeHide") as ModuleAttachNodeHide;
            if(hideModule == null)
            {
                log.error("PartModule creation broke and returned null.");
                return null;
            }
            hideModule.EditorAwake();
            hideModule.setNode(node);
            if (hideModule.node == null)
            {
                node.owner.RemoveModule(hideModule);
                log.warning("The node wasn't set successfully, destroying the PartModule.");
                return null;
            }

            return hideModule;
        }

        public void showNode(AttachNode node)
        {
            if(node==null)
            {
                log.error("Node is null!");
                return;
            }
            if (!hidingModules.ContainsKey(node))
            {
                log.error("Node does not exist in hidden nodes list!");
                node.nodeType = AttachNode.NodeType.Stack;
                node.radius = 0.4f;
                return;
            }
            hidingModules[node].show();
        }
    }
}
