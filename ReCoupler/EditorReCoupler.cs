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
        public static EditorReCoupler instance;

        Logger log = new Logger("EditorReCoupler: ");

        public Dictionary<AttachNode, sizeType> hiddenNodes = new Dictionary<AttachNode, sizeType>();
        public Dictionary<AttachNode, AttachNode> nodePairs = new Dictionary<AttachNode, AttachNode>();
        public List<AttachNode> openNodes = new List<AttachNode>();

        void Awake()
        {
            if (instance)
                Destroy(instance);
            instance = this;
        }

        void Start()
        {
            log.debug("Registering GameEvents.");
            GameEvents.onEditorPartEvent.Add(OnEditorPartEvent);
            GameEvents.onEditorRedo.Add(OnEditorUnRedo);
            GameEvents.onEditorUndo.Add(OnEditorUnRedo);
            GameEvents.onEditorLoad.Add(OnEditorLoad);

            //GameEvents.onEditorShipModified.Add(OnEditorShipModified);
        }

        public void OnDestroy()
        {
            log.debug("Unregistering GameEvents.");
            GameEvents.onEditorPartEvent.Remove(OnEditorPartEvent);
            GameEvents.onEditorRedo.Remove(OnEditorUnRedo);
            GameEvents.onEditorUndo.Remove(OnEditorUnRedo);
            GameEvents.onEditorLoad.Remove(OnEditorLoad);
            GameEvents.onEditorRestart.Add(OnEditorRestart);
            GameEvents.onLevelWasLoaded.Add(OnLevelWasLoaded);

            //GameEvents.onEditorShipModified.Remove(OnEditorShipModified);
            showAllNodes();
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
                hiddenNodes.Clear();
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
                AttachNode closestNode = ReCouplerManager.getEligiblePairing(partNodes[i], openNodes);
                if (closestNode != null)
                {
                    nodePairs.Add(partNodes[i], closestNode);
                    nodePairs.Add(closestNode, partNodes[i]);
                    hideNode(partNodes[i]);
                    hideNode(closestNode);
                    openNodes.Remove(closestNode);
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
                foreach(AttachNode node in part.attachNodes)
                {
                    if (node.radius == 0.001f)
                    {
                        log.warning("A part with a hidden node was copied. This is a problem!");
                        node.radius = 0.4f;
                        node.nodeType = AttachNode.NodeType.Stack;

                        ModuleAttachNodeHide hideModule = part.AddModule("ModuleAttachNodeHide") as ModuleAttachNodeHide;
                    }
                }
            }
        }

        public void showAllNodes()
        {
            for(int i = hiddenNodes.Count-1; i>=0; i--)
            {
                AttachNode node = hiddenNodes.Keys.ElementAt(i);
                if (node != null)
                    showNode(node);
                else
                {
                    log.error("hiddenNode element was null!");
                    hiddenNodes.Remove(node);
                }
            }
            //foreach (AttachNode node in hiddenNodes.Keys)
                //showNode(node);
        }

        public void hideNode(AttachNode node)
        {
            if (hiddenNodes.ContainsKey(node))
                return;

            log.debug("Hiding node on " + node.owner.name);
            hiddenNodes.Add(node, new sizeType(node.radius, node.nodeType));
            node.nodeType = AttachNode.NodeType.Dock;
            node.radius = 0.001f;
        }

        public void showNode(AttachNode node)
        {
            if(node==null)
            {
                log.error("Node is null!");
                hiddenNodes.Remove(node);
                return;
            }
            if (!hiddenNodes.ContainsKey(node))
            {
                log.error("Node does not exist in hidden nodes list!");
                node.nodeType = AttachNode.NodeType.Stack;
                node.radius = 0.4f;
                return;
            }
            node.nodeType = hiddenNodes[node].type;
            node.radius = hiddenNodes[node].size;
            hiddenNodes.Remove(node);
        }
    }

    public struct sizeType
    {
        public float size;
        public AttachNode.NodeType type;
        public sizeType(float size = 0.4f, AttachNode.NodeType type = AttachNode.NodeType.Stack)
        {
            this.size = size;
            this.type = type;
        }
    }
}
