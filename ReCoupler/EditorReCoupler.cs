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

        //public Dictionary<AttachNode, ModuleAttachNodeHide> hidingModules = new Dictionary<AttachNode, ModuleAttachNodeHide>();
        public List<AttachNode[]> nodePairs = new List<AttachNode[]>();
        public List<AttachNode> openNodes = new List<AttachNode>();
        public List<hideData> hiddenNodes = new List<hideData>();

        public float connectRadius = ReCouplerSettings.connectRadius_default;
        public float connectAngle = ReCouplerSettings.connectAngle_default;

        public bool anyhidden = false;
        public bool allHidden = false;

        public class hideData
        {
            public AttachNode node;
            public float size;
            public AttachNode.NodeType nodeType;
            bool hidden;

            public hideData(AttachNode node, float size, AttachNode.NodeType nodeType, bool hidden = false)
            {
                this.node = node;
                this.size = size;
                this.nodeType = nodeType;
                this.hidden = hidden;
                EditorReCoupler.Instance.anyhidden |= this.hidden;
            }
            public hideData(AttachNode node)
            {
                this.node = node;
                this.size = node.radius;
                this.nodeType = node.nodeType;
                if (this.size == 0.01f)
                {
                    this.hidden = true;
                    this.size = 0.4f;
                    this.nodeType = AttachNode.NodeType.Stack;
                }
                else
                    this.hidden = false;
                EditorReCoupler.Instance.anyhidden |= this.hidden;
            }

            public void hideNode()
            {
                if (hidden)
                    return;
                hidden = true;
                EditorReCoupler.Instance.anyhidden = true;
                node.radius = 0.01f;
                node.nodeType = AttachNode.NodeType.Dock;
            }

            public void showNode()
            {
                if (!hidden)
                    return;
                node.radius = size;
                node.nodeType = nodeType;
                hidden = false;
                EditorReCoupler.Instance.allHidden = false;
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
            showAllNodes();
        }

        private void OnGameSceneSwitchRequested(GameEvents.FromToAction<GameScenes, GameScenes> FromToData)
        {
            showAllNodes();
            if (FromToData.from == GameScenes.EDITOR)
            {
                for (int i = hiddenNodes.Count - 1; i >= 0; i--)
                {
                    hiddenNodes[i].showNode();
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
                    nodePairs.Add(new AttachNode[] { partNodes[i], closestNode });
                    hiddenNodes.Add(new hideData(partNodes[i]));
                    hiddenNodes.Add(new hideData(closestNode));
                    //hideNode(partNodes[i]);
                    //hideNode(closestNode);
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

            showAllNodes();
            hiddenNodes.Clear();
            for(int i = nodePairs.Count - 1; i>=0; i--)
            {
                ConnectedLivingSpacesCompatibility.RequestRemoveConnection(nodePairs[i][0].owner, nodePairs[i][1].owner);
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
            {
                reConstruct();
                showAllNodes();
            }
            else if (constEvent == ConstructionEventType.PartDragging)
                hideAllNodes();
            else
                showAllNodes();

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

        public void showAllNodes()
        {
            if (anyhidden == false)
                return;
            for(int i = hiddenNodes.Count-1; i>=0; i--)
            {
                if (hiddenNodes[i].node != null)
                    hiddenNodes[i].showNode();
                else
                {
                    log.error("hidingModules element was null!");
                    hiddenNodes.Remove(hiddenNodes[i]);
                }
            }
            allHidden = false;
            anyhidden = false;
        }

        public void hideAllNodes()
        {
            if (allHidden == true)
                return;
            for (int i = hiddenNodes.Count - 1; i >= 0; i--)
            {
                if (hiddenNodes[i].node != null)
                    hiddenNodes[i].hideNode();
                else
                {
                    log.error("hidingModules element was null!");
                    hiddenNodes.Remove(hiddenNodes[i]);
                }
            }
            allHidden = true;
            anyhidden = true;
        }

        public void hideNode(AttachNode node)
        {
            if (!hiddenNodes.Any(hiddenNode => hiddenNode.node == node))
                hiddenNodes.Add(new hideData(node));
            if (hiddenNodes.Any(hiddenNode => hiddenNode.node == node))
            {
                log.debug("Hiding node on " + node.owner.name);
                hiddenNodes.FirstOrDefault(hiddenNode => hiddenNode.node == node).hideNode();
            }
        }

        public void showNode(AttachNode node)
        {
            if(node==null)
            {
                log.error("Node is null!");
                return;
            }
            if (!hiddenNodes.Any(hiddenNode => hiddenNode.node == node))
            {
                log.error("Node does not exist in hidden nodes list!");
                node.nodeType = AttachNode.NodeType.Stack;
                node.radius = 0.4f;
                return;
            }
            hiddenNodes.FirstOrDefault(hiddenNode => hiddenNode.node == node).showNode();
        }
    }
}
