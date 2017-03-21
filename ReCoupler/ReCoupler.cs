using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ReCoupler
{
    public class ReCouplerManager : VesselModule
    {
        static Logger log = new Logger("ReCoupler: ");

        public List<JointTracker> joints = new List<JointTracker>();
        public Dictionary<ModuleDecouple, JointTracker> decouplersInvolved = new Dictionary<ModuleDecouple, JointTracker>();

        public const float connectRadius_default = 0.1f;
        public const float connectAngle_default = 91;
        protected internal const string configURL = "ReCoupler/ReCouplerSettings/ReCouplerSettings";

        public float connectRadius = connectRadius_default;
        public float connectAngle = connectAngle_default;

        protected bool checkCoupleNextFrame = false;
        protected bool checkBreakNextFrame = false;
        protected List<int[]> storedData = new List<int[]>();
        public bool started = true;
        
        new public void Start()
        {
            LoadSettings(out connectRadius, out connectAngle);

            destroyAllJoints();
            joints.Clear();
            decouplersInvolved.Clear();
            checkCoupleNextFrame = false;
            checkBreakNextFrame = false;

            if (!vessel.loaded || !HighLogic.LoadedSceneIsFlight)
            {
                //log.debug("Not instantiating for " + vessel.vesselName);
                started = false;
                return;
            }
            log.debug("Start() " + vessel.vesselName + " : " + Planetarium.GetUniversalTime());
            GameEvents.onVesselPartCountChanged.Add(OnVesselPartCountChanged);
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
            GameEvents.onJointBreak.Add(OnJointBreak);

            List<Part> parts = vessel.Parts;
            for (int i = 0; i<storedData.Count; i++)
            {
                parts[storedData[i][0]].attachNodes[storedData[i][1]].attachedPart = null;
            }

            generateJoints();
            log.debug("Checking CLS Installation.");
            bool isCLSInstalled = ConnectedLivingSpacesCompatibility.IsCLSInstalled;
        }

        protected override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            
            if (joints.Count > 0)
            {
                log.debug("OnSave: " + vessel.vesselName);
                List<Part> parts = vessel.Parts;
                log.debug("Saving " + joints.Count + " nodes.");

                for (int i = 0; i < joints.Count; i++)
                {
                    ConfigNode jointTrackerNode = node.AddNode("RECOUPLER");
                    jointTrackerNode.AddValue("partID", parts.IndexOf(joints[i].parts[0]));
                    jointTrackerNode.AddValue("nodeID", joints[i].parts[0].attachNodes.IndexOf(joints[i].nodes[0]));
                    jointTrackerNode.AddValue("partID", parts.IndexOf(joints[i].parts[1]));
                    jointTrackerNode.AddValue("nodeID", joints[i].parts[1].attachNodes.IndexOf(joints[i].nodes[1]));
                }
            }
            else if (storedData.Count > 0)
            {
                log.debug("OnSave: " + vessel.vesselName);
                log.debug("Saving " + storedData.Count + " nodes.");

                for (int i = 0; i < storedData.Count; i += 2)
                {
                    ConfigNode jointTrackerNode = node.AddNode("RECOUPLER");
                    jointTrackerNode.AddValue("partID", storedData[i][0]);
                    jointTrackerNode.AddValue("nodeID", storedData[i][1]);
                    jointTrackerNode.AddValue("partID", storedData[i + 1][0]);
                    jointTrackerNode.AddValue("nodeID", storedData[i + 1][1]);
                }
            }
        }

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            foreach (ConfigNode jointTrackerNode in node.GetNodes("RECOUPLER"))
            {
                string[] parse = jointTrackerNode.GetValues("partID");
                int[] partsID = new int[2];
                if (!(int.TryParse(parse[0], out partsID[0]) && (int.TryParse(parse[1], out partsID[1]))))
                {
                    log.error("Parsing failed");
                    continue;
                }
                parse = jointTrackerNode.GetValues("nodeID");
                int[] nodesID = new int[2];
                if (!(int.TryParse(parse[0], out nodesID[0]) && (int.TryParse(parse[1], out nodesID[1]))))
                {
                    log.error("Parsing failed");
                    continue;
                }

                storedData.Add(new int[2] { partsID[0], nodesID[0] });
                storedData.Add(new int[2] { partsID[1], nodesID[1] });
            }
        }

        public static void LoadSettings(out float loadedRadius, out float loadedAngle)
        {
            var cfgs = GameDatabase.Instance.GetConfigs("ReCouplerSettings");
            if (cfgs.Length > 0)
            {
                if (!float.TryParse(cfgs[0].config.GetValue("connectRadius"), out loadedRadius))
                    loadedRadius = connectRadius_default;

                if (!float.TryParse(cfgs[0].config.GetValue("connectAngle"), out loadedAngle))
                    loadedAngle = connectAngle_default;
                
                if (!cfgs[0].url.Equals(configURL))
                {
                    for (int i = 0; i < cfgs.Length; i++)
                    {
                        if (cfgs[i].url.Equals(configURL))
                        {
                            if (!float.TryParse(cfgs[i].config.GetValue("connectRadius"), out loadedRadius))
                                loadedRadius = connectRadius_default;

                            if (!float.TryParse(cfgs[i].config.GetValue("connectAngle"), out loadedAngle))
                                loadedAngle = connectAngle_default;
                            break;
                        }
                    }
                }
            }
            else
            {
                loadedRadius = connectRadius_default;
                loadedAngle = connectAngle_default;
                log.warning("Couldn't find the settings, using the defaults.");
            }
        }

        private void OnJointBreak(EventReport data)
        {
            for (int i = 0; i < joints.Count; i++)
            {
                JointTracker jt = joints[i];
                if (jt.parts[0] == data.origin || jt.parts[1] == data.origin)
                {
                    if (jt.joint == null)
                    {
                        jt.destroyLink();
                        joints.Remove(jt);
                    }
                    else
                    {
                        checkBreakNextFrame = true;
                        this.vessel.StartCoroutine(WaitAndCheckBreak(jt));
                    }
                    break;
                }
            }
        }

        IEnumerator WaitAndCheckBreak(JointTracker jt)
        {
            if (checkBreakNextFrame == true)
            {
                log.debug("Checking next frame");
                checkBreakNextFrame = false;
                yield return new WaitForFixedUpdate();
            }
            log.debug("Checking this frame");

            if (jt.joint == null)
            {
                jt.destroyLink();
                joints.Remove(jt);
            }
        }

        public void OnVesselGoOffRails(Vessel modifiedVessel)
        {
            if (modifiedVessel != this.vessel)
                return;

            log.debug("OnVesselGoOffRails: " + modifiedVessel.vesselName);
            generateJoints();       // To get any new joints. Old ones still kept.
            foreach (JointTracker jt in joints)
            {
                jt.createLink();
            }
        }

        public void OnDestroy()
        {
            if (started)
            {
                log.debug("OnDestroy()");
                GameEvents.onVesselPartCountChanged.Remove(OnVesselPartCountChanged);
                GameEvents.onVesselCreate.Remove(OnVesselCreate);
                GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
                GameEvents.onJointBreak.Remove(OnJointBreak);
                destroyAllJoints();
            }
        }

        private void OnVesselCreate(Vessel newVessel)
        {
            checkCoupleNextFrame = true;
            this.vessel.StartCoroutine(WaitAndCheckCouple());
        }

        IEnumerator WaitAndCheckCouple()
        {
            if (checkCoupleNextFrame == true)
            {
                log.debug("Checking next frame");
                checkCoupleNextFrame = false;
                yield return new WaitForFixedUpdate();
            }
            log.debug("Checking this frame");

            for (int i = decouplersInvolved.Count - 1; i >= 0; i--)
            {
                KeyValuePair<ModuleDecouple, JointTracker> decouplerPair = decouplersInvolved.ElementAt(i);

                if (decouplerPair.Key == null || decouplerPair.Value == null)
                {
                    decouplersInvolved.Remove(decouplerPair.Key);
                    continue;
                }
                if (decouplerPair.Key.isDecoupled)
                {
                    log.debug("Decoupler " + decouplerPair.Key.part.name + " decoupled. Removing from joints list.");
                    decouplerPair.Value.destroyLink();
                    joints.Remove(decouplerPair.Value);
                    decouplersInvolved.Remove(decouplerPair.Key);
                }
            }

            JointTracker jt;
            bool combinedAny = false;
            for (int i = joints.Count - 1; i >= 0; i--)
            {
                jt = joints[i];
                if (jt.parts[0] == null || jt.parts[1] == null)
                {
                    log.debug("Removing a null joint.");
                    jt.destroyLink();
                    joints.Remove(jt);
                    continue;
                }
                if (jt.joint == null)
                {
                    log.debug("A joint must have broken.");
                    jt.destroyLink();
                    joints.Remove(jt);
                    continue;
                }

                log.debug(jt.parts[0].name + " / " + jt.parts[1].name);
                log.debug(jt.parts[0].vessel + " / " + jt.parts[1].vessel);

                if (jt.parts[0].vessel != jt.parts[1].vessel)
                {
                    log.debug("Vessel tree split, but should be re-combined.");
                    // The vessel split at the official tree, but isn't split at this joint.

                    //continue;
                    Part targetPart;
                    if (jt.parts[0].vessel == this.vessel || jt.parts[1].vessel == this.vessel)
                        targetPart = jt.parts[0].vessel == this.vessel ? jt.parts[0] : jt.parts[1];
                    else
                        targetPart = jt.parts[0].vessel.Parts.Count >= jt.parts[1].vessel.Parts.Count ? jt.parts[0] : jt.parts[1];

                    Part couplePart = targetPart == jt.parts[0] ? jt.parts[1] : jt.parts[0];
                    AttachNode targetNode = targetPart == jt.parts[0] ? jt.nodes[0] : jt.nodes[1];
                    AttachNode coupleNode = targetPart == jt.parts[0] ? jt.nodes[1] : jt.nodes[0];

                    log.debug("Coupling " + couplePart.name + " to " + targetPart.name + ".");

                    CoupleParts(coupleNode, targetNode);
                    combinedAny = true;
                    
                    //targetPart.vessel.currentStage = KSP.UI.Screens.StageManager.RecalculateVesselStaging(targetPart.vessel);
                    vessel.currentStage = KSP.UI.Screens.StageManager.RecalculateVesselStaging(vessel) + 1;
                }

                if (jt.parts[0].vessel != this.vessel && jt.parts[1].vessel != this.vessel)
                {
                    log.debug("Removing joint between " + jt.parts[0].name + " and " + jt.parts[1].name + " because they are no longer part of this vessel.");
                    jt.destroyLink();
                    joints.Remove(jt);
                    continue;
                }
            }

            if (combinedAny)
            {
                for (int i = joints.Count - 1; i >= 0; i--)
                {
                    jt = joints[i];
                    jt.combineCrossfeedSets();
                }
            }
        }
        
        public void OnVesselPartCountChanged(Vessel modifiedVessel)
        {
            if (modifiedVessel != this.vessel)
                return;

            log.debug("onVesselPartCountChanged(): " + this.vessel.vesselName + "; " + this.vessel.parts.Count + " parts.");
            bool aboutToRunAgain = false;

            foreach (KeyValuePair<ModuleDecouple, JointTracker> decouplerPair in decouplersInvolved)
            {
                if (decouplerPair.Key.isDecoupled)
                {
                    log.debug("Decoupler " + decouplerPair.Key.part.name + " decoupled. Removing from joints list.");
                    decouplerPair.Value.destroyLink();
                    joints.Remove(decouplerPair.Value);
                    decouplersInvolved.Remove(decouplerPair.Key);
                }
            }

            if (!aboutToRunAgain)
            {
                //destroyAllJoints();
                generateJoints();
            }
        }

        public void generateJoints()
        {            
            log.debug("Generating joints");
            decouplersInvolved.Clear();

            List<AttachNode> openNodes = findOpenNodes(this.vessel);
            // Removing nodes that we have as part of fake links:
            foreach(JointTracker jt in joints)
            {
                openNodes.Remove(jt.nodes[0]);
                openNodes.Remove(jt.nodes[1]);
                foreach(ModuleDecouple decoupler in jt.decouplers)
                {
                    decouplersInvolved.Add(decoupler, jt);
                }
            }

            log.debug(openNodes.Count + " open nodes.");
            Dictionary<AttachNode, AttachNode> eligibleNodes = getEligibleNodes(openNodes, connectRadius, connectAngle);
            foreach (AttachNode fromNode in eligibleNodes.Keys)
            {
                openNodes.Remove(fromNode);
                openNodes.Remove(eligibleNodes[fromNode]);
                log.debug("Creating link between " + fromNode.owner.name + " and " + eligibleNodes[fromNode].owner.name + ".");

                ModuleDockingNode fromDockingPort, toDockingPort;

                if (hasDockingPort(fromNode, out fromDockingPort) && hasDockingPort(eligibleNodes[fromNode], out toDockingPort))
                {
                    //fromDockingPort.DockToSameVessel(toDockingPort);
                    continue;
                }

                JointTracker newJT = new JointTracker(fromNode, eligibleNodes[fromNode], !vessel.packed);

                joints.Add(newJT);

                newJT.combineCrossfeedSets();

                foreach (ModuleDecouple decoupler in newJT.decouplers)
                {
                    decouplersInvolved.Add(decoupler, newJT);
                }
            }
        }

        public void destroyAllJoints()
        {
            foreach(JointTracker joint in joints)
            {
                joint.destroyLink();
            }
            joints.Clear();
        }

        public static List<AttachNode> findOpenNodes(Vessel vessel)
        {
            return findOpenNodes(vessel.Parts);
        }

        public static List<AttachNode> findOpenNodes(List<Part> partList)
        {
            List<AttachNode> openNodes = new List<AttachNode>();
            for(int i = 0; i<partList.Count; i++)
            {
                openNodes.AddRange(findOpenNodes(partList[i]));
            }
            return openNodes;
        }

        public static List<AttachNode> findOpenNodes(Part[] partList)
        {
            List<AttachNode> openNodes = new List<AttachNode>();
            for (int i = 0; i < partList.Length; i++)
            {
                openNodes.AddRange(findOpenNodes(partList[i]));
            }
            return openNodes;
        }

        public static List<AttachNode> findOpenNodes(Part part)
        {
            List<AttachNode> openNodes = new List<AttachNode>();
            for (int i = 0; i < part.attachNodes.Count; i++)
            {
                if (part.attachNodes[i].nodeType == AttachNode.NodeType.Surface)
                    continue;

                if (part.attachNodes[i].attachedPart != null)
                    continue;

                // Dealing with fairing interstage nodes:
                if (part.attachNodes[i].id.StartsWith("interstage"))
                {
                    log.debug("Skipping interstage node: " + part.attachNodes[i].id + " on part " + part.name);
                    continue;
                }

                if (HighLogic.LoadedScene == GameScenes.FLIGHT)
                {
                    bool doNotJoint = false;

                    foreach (ModuleDecouple decoupler in part.FindModulesImplementing<ModuleDecouple>())
                    {
                        if (decoupler.isDecoupled && (part.attachNodes[i] == decoupler.ExplosiveNode || decoupler.isOmniDecoupler))
                        {
                            doNotJoint = true;
                            break;
                        }
                    }
                    foreach (ModuleDockingNode dockingNode in part.FindModulesImplementing<ModuleDockingNode>())
                    {
                        if (dockingNode.referenceNode == null)
                        {
                            log.error("Docking node has null referenceNode!");
                            continue;
                        }
                        if (dockingNode.referenceNode == part.attachNodes[i])
                        {
                            if (dockingNode.otherNode != null)
                                doNotJoint = true;
                            break;
                        }
                    }

                    foreach (ModuleCargoBay cargoBay in part.FindModulesImplementing<ModuleCargoBay>())
                    {
                        if (cargoBay.nodeInnerForeID == part.attachNodes[i].id || cargoBay.nodeInnerAftID == part.attachNodes[i].id)
                        {
                            doNotJoint = true;
                            break;
                        }
                    }

                    if (doNotJoint)
                        continue;
                }
                // On revert to launch/some loads, certain AttachNode.owner's were null.
                // This doesn't make sense since they should be the part it is on.
                // We'll just fix that so we don't get nullRefs.
                if (part.attachNodes[i].owner == null)
                    part.attachNodes[i].owner = part;

                openNodes.Add(part.attachNodes[i]);
            }
            return openNodes;
        }

        public static Dictionary<AttachNode, AttachNode> getEligibleNodes(List<AttachNode> nodes, float radius = connectRadius_default, float angle = connectAngle_default)
        {
            Dictionary<AttachNode, AttachNode> eligiblePairs = new Dictionary<AttachNode, AttachNode>();
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                if (eligiblePairs.ContainsValue(nodes[i]) || eligiblePairs.ContainsKey(nodes[i]))
                {
                    log.warning("Node is already part of a eligible pair!");
                    continue;
                }

                AttachNode closestNode = getEligiblePairing(nodes[i], nodes.GetRange(i + 1, nodes.Count - (i + 1)), radius, angle);

                if (closestNode != null)
                {
                    eligiblePairs.Add(nodes[i], closestNode);
                }
            }
            return eligiblePairs;
        }

        public static AttachNode getEligiblePairing(AttachNode node, List<AttachNode> checkNodes, float radius = connectRadius_default, float angle = connectAngle_default)
        {
            float closestDist = radius;
            AttachNode closestNode = null;

            for (int j = 0; j < checkNodes.Count; j++)
            {
                if (node.owner == checkNodes[j].owner)
                    continue; // Nodes on the same Part are not eligible.
                if (node.owner.parent == checkNodes[j].owner || checkNodes[j].owner.parent == node.owner)
                    continue; // Parent-child relationships don't need doubling up.

                /*float dist = (Part.PartToVesselSpacePos(node.position, node.owner, node.owner.vessel, PartSpaceMode.Pristine) -
                    Part.PartToVesselSpacePos(checkNodes[j].position, checkNodes[j].owner, checkNodes[j].owner.vessel, PartSpaceMode.Pristine)).magnitude;
                float angle = Vector3.Angle(Part.PartToVesselSpaceDir(node.orientation, node.owner, node.owner.vessel, PartSpaceMode.Pristine),
                    Part.PartToVesselSpaceDir(checkNodes[j].orientation, checkNodes[j].owner, checkNodes[j].owner.vessel, PartSpaceMode.Pristine));*/
                float dist = ((node.owner.transform.rotation * node.position + node.owner.transform.position) -
                    (checkNodes[j].owner.transform.rotation * checkNodes[j].position + checkNodes[j].owner.transform.position)).magnitude;
                float angleBtwn = Vector3.Angle(node.owner.transform.rotation * node.orientation, checkNodes[j].owner.transform.rotation * checkNodes[j].orientation);

                //log.debug(node.owner.name + "/" + checkNodes[j].owner.name + ": " + dist + "m, at " + angle + " deg.");

                if (dist <= closestDist && Math.Abs(angleBtwn - 180) <= angle)
                // but at least closer than the min radius because of the initialization of closestDist
                {
                    log.debug(node.owner.name + "/" + checkNodes[j].owner.name + ": " + dist + "m, at " + angleBtwn + " deg.");
                    closestNode = checkNodes[j];
                }
            }

            return closestNode;
        }

        // Adapted from KISv1 by IgorZ
        // See https://github.com/ihsoft/KAS/tree/KAS-v1.0
        // This method is in the public domain: https://github.com/ihsoft/KAS/blob/KAS-v1.0/LICENSE-1.0.md

        public static void CoupleParts(AttachNode sourceNode, AttachNode targetNode)
        {
            var srcPart = sourceNode.owner;
            var srcVessel = srcPart.vessel;
            var trgPart = targetNode.owner;
            var trgVessel = trgPart.vessel;

            var vesselInfo = new DockedVesselInfo();
            vesselInfo.name = srcVessel.vesselName;
            vesselInfo.vesselType = srcVessel.vesselType;
            vesselInfo.rootPartUId = srcVessel.rootPart.flightID;

            GameEvents.onActiveJointNeedUpdate.Fire(srcVessel);
            GameEvents.onActiveJointNeedUpdate.Fire(trgVessel);
            sourceNode.attachedPart = trgPart;
            targetNode.attachedPart = srcPart;
            srcPart.attachMode = AttachModes.STACK;  // All KAS links are expected to be STACK.
            srcPart.Couple(trgPart);
            // Depending on how active vessel has updated do either force active or make active. Note, that
            // active vessel can be EVA kerbal, in which case nothing needs to be adjusted.    
            // FYI: This logic was taken from ModuleDockingNode.DockToVessel.
            if (srcVessel == FlightGlobals.ActiveVessel)
            {
                FlightGlobals.ForceSetActiveVessel(sourceNode.owner.vessel);  // Use actual vessel.
                //FlightInputHandler.SetNeutralControls();
            }
            else if (sourceNode.owner.vessel == FlightGlobals.ActiveVessel)
            {
                sourceNode.owner.vessel.MakeActive();
                //FlightInputHandler.SetNeutralControls();
            }
            GameEvents.onVesselWasModified.Fire(sourceNode.owner.vessel);

            //return vesselInfo;
        }

        public static bool hasDockingPort(AttachNode node, out ModuleDockingNode dockingPort)
        {
            dockingPort = null;
            foreach(ModuleDockingNode moduleDockingNode in node.owner.FindModulesImplementing<ModuleDockingNode>())
            {
                if (moduleDockingNode.referenceNode == node)
                {
                    dockingPort = moduleDockingNode;
                    return true;
                }
            }
            return false;
        }


        public class JointTracker
        {
            public ConfigurableJoint joint = null;
            public List<AttachNode> nodes;
            public List<Part> parts;

            Logger log;

            private List<ModuleDecouple> cachedDecouplers = null;

            public List<ModuleDecouple> decouplers
            {
                get
                {
                    if (cachedDecouplers == null)
                    {
                        cachedDecouplers = new List<ModuleDecouple>();
                        for (int i = 0; i < parts.Count; i++)
                        {
                            foreach (ModuleDecouple decoupler in parts[i].FindModulesImplementing<ModuleDecouple>())
                            {
                                if (decoupler.isOmniDecoupler || decoupler.ExplosiveNode == nodes[i])
                                    cachedDecouplers.Add(decoupler);
                            }
                        }
                    }
                    return cachedDecouplers;
                }
            }

            /*public JointTracker(ConfigurableJoint joint, Part[] parts, AttachNode[] nodes)
            {
                this.joint = joint;
                this.parts = parts;
                this.nodes = nodes;
            }*/

            public JointTracker(ConfigurableJoint joint, AttachNode parentNode, AttachNode childNode, bool link = true)
            {
                this.joint = joint;
                this.nodes = new List<AttachNode> { parentNode, childNode };
                this.parts = new List<Part> { parentNode.owner, childNode.owner };
                log = new Logger("ReCoupler: JointTracker: " + parts[0].name + " and " + parts[1].name);
                if (link)
                    this.createLink();
                this.setNodeAero();
            }

            public JointTracker(AttachNode parentNode, AttachNode childNode, bool link = true)
            {
                this.nodes = new List<AttachNode> { parentNode, childNode };
                this.parts = new List<Part> { parentNode.owner, childNode.owner };
                log = new Logger("ReCoupler: JointTracker: " + parts[0].name + " and " + parts[1].name + " ");
                if (link)
                    this.createLink();
                this.setNodeAero();
            }

            public void setNodeAero()
            {
                uint[] oldIDs = new uint[] { nodes[0].attachedPartId, nodes[1].attachedPartId };
                log.debug(oldIDs[0] + " " + oldIDs[1] + ": " + parts[0].attachNodes.Count);
                nodes[0].attachedPart = parts[1];
                nodes[0].attachedPartId = parts[1].flightID;
                nodes[1].attachedPart = parts[0];
                nodes[1].attachedPartId = parts[0].flightID;
            }

            public ConfigurableJoint createLink()
            {
                if (this.joint != null)
                {
                    log.warning("This link already has a joint object.");
                    return this.joint;
                }

                AttachNode parent = this.nodes[0];
                AttachNode child = this.nodes[1];
                Part parentPart = this.parts[0];
                Part childPart = this.parts[1];

                log.debug("Creating joint between " + parentPart.name + " and " + childPart.name + ".");

                if (parentPart.Rigidbody == null)
                    log.error("parentPart body is null :o");
                if (childPart.Rigidbody == null)
                    log.error("childPart body is null :o");

                ConfigurableJoint newJoint;

                newJoint = childPart.gameObject.AddComponent<ConfigurableJoint>();
                newJoint.connectedBody = parentPart.Rigidbody;
                newJoint.anchor = Vector3.zero;                 // There's probably a better anchor point, like the attachNode...
                newJoint.connectedAnchor = Vector3.zero;

                newJoint.autoConfigureConnectedAnchor = false;  // Probably don't need.
                newJoint.axis = Vector3.up;
                newJoint.secondaryAxis = Vector3.left;
                newJoint.enableCollision = false;               // Probably don't need.

                newJoint.breakForce = float.PositiveInfinity;   //Math.Min(parentPart.breakingForce, childPart.breakingForce);
                newJoint.breakTorque = float.PositiveInfinity;  //Math.Min(parentPart.breakingTorque, childPart.breakingTorque);

                newJoint.angularXMotion = ConfigurableJointMotion.Limited;
                newJoint.angularYMotion = ConfigurableJointMotion.Limited;
                newJoint.angularZMotion = ConfigurableJointMotion.Limited;

                JointDrive linearDrive = new JointDrive();
                linearDrive.maximumForce = 1E20f;
                linearDrive.positionDamper = 0;
                linearDrive.positionSpring = 1E20f;
                newJoint.xDrive = linearDrive;
                newJoint.yDrive = linearDrive;
                newJoint.zDrive = linearDrive;

                newJoint.projectionDistance = 0.1f;
                newJoint.projectionAngle = 180;
                newJoint.projectionMode = JointProjectionMode.None;

                newJoint.rotationDriveMode = RotationDriveMode.XYAndZ;
                newJoint.swapBodies = false;
                newJoint.targetAngularVelocity = Vector3.zero;
                newJoint.targetPosition = Vector3.zero;
                newJoint.targetVelocity = Vector3.zero;
                newJoint.targetRotation = new Quaternion(0, 0, 0, 1);

                JointDrive angularDrive = new JointDrive();
                angularDrive.maximumForce = 1E20f;
                angularDrive.positionSpring = 60000;
                angularDrive.positionDamper = 0;
                newJoint.angularXDrive = angularDrive;
                newJoint.angularYZDrive = angularDrive;

                SoftJointLimitSpring zeroSpring = new SoftJointLimitSpring();
                zeroSpring.spring = 0;
                zeroSpring.damper = 0;
                newJoint.angularXLimitSpring = zeroSpring;
                newJoint.angularYZLimitSpring = zeroSpring;

                SoftJointLimit angleSoftLimit = new SoftJointLimit();
                angleSoftLimit.bounciness = 0;
                angleSoftLimit.contactDistance = 0;
                angleSoftLimit.limit = 177;
                newJoint.angularYLimit = angleSoftLimit;
                newJoint.angularZLimit = angleSoftLimit;
                newJoint.highAngularXLimit = angleSoftLimit;
                newJoint.lowAngularXLimit = angleSoftLimit;

                this.joint = newJoint;
                this.setNodeAero();

                ConnectedLivingSpacesCompatibility.RequestAddConnection(this.parts[0], this.parts[1]);

                return this.joint;
            }

            public static void combineCrossfeedSets(Part parent, Part child)
            {
                if (parent.crossfeedPartSet == null)
                    return;
                if (parent.crossfeedPartSet.ContainsPart(child))
                    return;

                //log.debug("Combining Crossfeed Sets.");
                HashSet<Part> partsToAdd = parent.crossfeedPartSet.GetParts();
                partsToAdd.UnionWith(child.crossfeedPartSet.GetParts());

                parent.crossfeedPartSet.RebuildParts(partsToAdd);
                child.crossfeedPartSet.RebuildParts(partsToAdd);
                parent.crossfeedPartSet.RebuildInPlace();
                child.crossfeedPartSet.RebuildInPlace();
            }

            public void combineCrossfeedSets()
            {
                if (parts[0].crossfeedPartSet == null)
                    return;
                if (parts[0].crossfeedPartSet.ContainsPart(parts[1]))
                    return;
                log.debug("Part Xfeed: " + parts[0].fuelCrossFeed + " node: " + nodes[0].ResourceXFeed);
                log.debug("Part Xfeed: " + parts[1].fuelCrossFeed + " node: " + nodes[1].ResourceXFeed);
                log.debug("Combining Crossfeed Sets.");
                combineCrossfeedSets(this.parts[0], this.parts[1]);
            }

            public void destroyLink()
            {
                log.debug("Destroying a link.");
                if (joint != null)
                    GameObject.Destroy(joint);

                if (parts[0] != null)
                    nodes[0].attachedPart = null;

                if (parts[1] != null)
                    nodes[1].attachedPart = null;

                if (this.parts[0] != null && this.parts[1] != null)
                    ConnectedLivingSpacesCompatibility.RequestRemoveConnection(this.parts[0], this.parts[1]);
            }
        }
    }
}
