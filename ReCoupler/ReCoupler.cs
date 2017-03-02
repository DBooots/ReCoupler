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

        public const float connectRadius = 0.1f;
        public const float connectAngle = 91;

        private bool checkNextFrame = false;
        public bool started = true;

        new public void Start()
        {
            destroyAllJoints();
            joints.Clear();
            decouplersInvolved.Clear();
            checkNextFrame = false;

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
            generateJoints();
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
                destroyAllJoints();
            }
        }

        private void OnVesselCreate(Vessel newVessel)
        {
            checkNextFrame = true;
            this.vessel.StartCoroutine(WaitAndCheck());
        }

        IEnumerator WaitAndCheck()
        {
            if (checkNextFrame == true)
            {
                log.debug("Checking next frame");
                checkNextFrame = false;
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

            for (int i = joints.Count - 1; i >= 0; i--)
            {
                JointTracker jt = joints[i];
                if(jt.parts[0]==null || jt.parts[1] == null)
                {
                    log.debug("Removing a null joint.");
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

                    CouplePart(couplePart, targetPart, coupleNode, targetNode);

                    targetPart.vessel.currentStage = KSP.UI.Screens.StageManager.RecalculateVesselStaging(targetPart.vessel);
                    vessel.currentStage = KSP.UI.Screens.StageManager.RecalculateVesselStaging(vessel);
                }

                if (jt.parts[0].vessel != this.vessel && jt.parts[1].vessel != this.vessel)
                {
                    log.debug("Removing joint between " + jt.parts[0].name + " and " + jt.parts[1].name + " because they are no longer part of this vessel.");
                    jt.destroyLink();
                    joints.Remove(jt);
                    continue;
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
            Dictionary<AttachNode, AttachNode> eligibleNodes = getEligibleNodes(openNodes);
            foreach (AttachNode fromNode in eligibleNodes.Keys)
            {
                openNodes.Remove(fromNode);
                openNodes.Remove(eligibleNodes[fromNode]);
                log.debug("Creating link between " + fromNode.owner.name + " and " + eligibleNodes[fromNode].owner.name + ".");

                JointTracker newJT = new JointTracker(fromNode, eligibleNodes[fromNode], !vessel.packed);

                joints.Add(newJT);

                combineCrossfeedSets(fromNode.owner, eligibleNodes[fromNode].owner);

                foreach (ModuleDecouple decoupler in newJT.decouplers)
                {
                    decouplersInvolved.Add(decoupler, newJT);
                }
            }
        }

        public static void combineCrossfeedSets(Part parent, Part child)
        {
            if (parent.crossfeedPartSet.ContainsPart(child))
                return;
            log.debug("Combining Crossfeed Sets.");
            HashSet<Part> partsToAdd = parent.crossfeedPartSet.GetParts();
            partsToAdd.UnionWith(child.crossfeedPartSet.GetParts());

            parent.crossfeedPartSet.RebuildParts(partsToAdd);
            child.crossfeedPartSet.RebuildParts(partsToAdd);
            parent.crossfeedPartSet.RebuildInPlace();
            child.crossfeedPartSet.RebuildInPlace();
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
            return findOpenNodes(vessel.parts);
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

                if (HighLogic.LoadedScene == GameScenes.FLIGHT)
                {
                    bool decoupled = false;

                    foreach (ModuleDecouple decoupler in part.FindModulesImplementing<ModuleDecouple>())
                    {
                        if (decoupler.isDecoupled && (part.attachNodes[i] == decoupler.ExplosiveNode || decoupler.isOmniDecoupler))
                        {
                            decoupled = true;
                            break;
                        }
                    }

                    if (decoupled)
                        continue;
                }
                openNodes.Add(part.attachNodes[i]);
            }
            return openNodes;
        }

        public static Dictionary<AttachNode, AttachNode> getEligibleNodes(List<AttachNode> nodes, float radius = connectRadius)
        {
            Dictionary<AttachNode, AttachNode> eligiblePairs = new Dictionary<AttachNode, AttachNode>();
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                if (eligiblePairs.ContainsValue(nodes[i]) || eligiblePairs.ContainsKey(nodes[i]))
                {
                    log.warning("Node is already part of a eligible pair!");
                    continue;
                }

                AttachNode closestNode = getEligiblePairing(nodes[i], nodes.GetRange(i + 1, nodes.Count - (i + 1)), radius);

                if (closestNode != null)
                {
                    eligiblePairs.Add(nodes[i], closestNode);
                }
            }
            return eligiblePairs;
        }

        public static AttachNode getEligiblePairing(AttachNode node, List<AttachNode> checkNodes, float radius = connectRadius)
        {
            float closestDist = radius;
            AttachNode closestNode = null;

            for (int j = 0; j < checkNodes.Count; j++)
            {
                if (node.owner == checkNodes[j].owner)
                    continue; // Nodes on the same Part are not eligible.

                /*float dist = (Part.PartToVesselSpacePos(node.position, node.owner, node.owner.vessel, PartSpaceMode.Pristine) -
                    Part.PartToVesselSpacePos(checkNodes[j].position, checkNodes[j].owner, checkNodes[j].owner.vessel, PartSpaceMode.Pristine)).magnitude;
                float angle = Vector3.Angle(Part.PartToVesselSpaceDir(node.orientation, node.owner, node.owner.vessel, PartSpaceMode.Pristine),
                    Part.PartToVesselSpaceDir(checkNodes[j].orientation, checkNodes[j].owner, checkNodes[j].owner.vessel, PartSpaceMode.Pristine));*/
                float dist = ((node.owner.transform.rotation * node.position + node.owner.transform.position) -
                    (checkNodes[j].owner.transform.rotation * checkNodes[j].position + checkNodes[j].owner.transform.position)).magnitude;
                float angle = Vector3.Angle(node.owner.transform.rotation * node.orientation, checkNodes[j].owner.transform.rotation * checkNodes[j].orientation);

                //log.debug(node.owner.name + "/" + checkNodes[j].owner.name + ": " + dist + "m, at " + angle + " deg.");

                if (dist <= closestDist && Math.Abs(angle - 180) <= connectAngle)
                // but at least closer than the min radius because of the initialization of closestDist
                {
                    log.debug(node.owner.name + "/" + checkNodes[j].owner.name + ": " + dist + "m, at " + angle + " deg.");
                    closestNode = checkNodes[j];
                }
            }

            return closestNode;
        }

        // Adapted from KIS by IgorZ and KospY
        // <summary>Couples parts of different vessels together.</summary>
        // <remarks>
        // When parts are compatible docking ports thet are docked instead of coupling. Docking ports
        // handle own logic on docking.
        // <para>
        // Parts will be coupled even if source and/or target attach node is incorrect. In such a case
        // the parts will be logically and physically joint into one vessel but normal parts interaction
        // logic may get broken (e.g. fuel flow).
        // </para>
        // </remarks>
        // <param name="srcPart">Source part to couple.</param>
        // <param name="tgtPart">New parent of the source part.</param>
        // <param name="srcAttachNodeId">
        // Attach node id on the source part. Can be <c>null</c> for the compatibility but it's an
        // erroneous situation and it will be logged.
        // </param>
        // <param name="tgtAttachNode">
        // Attach node on the parent to couple thru. Can be <c>null</c> for the compatibility but it's an
        // erroneous situation and it will be logged.
        // </param>
        public static void CouplePart(Part srcPart, Part tgtPart,
                                      AttachNode srcAttachNode = null,
                                      AttachNode tgtAttachNode = null)
        {
            // Node links.
            if (srcAttachNode.id != null)
            {
                if (srcAttachNode.id == "srfAttach")
                {
                    Debug.LogFormat("Attach type: {0} | ID : {1}",
                                    srcPart.srfAttachNode.nodeType, srcPart.srfAttachNode.id);
                    srcPart.attachMode = AttachModes.SRF_ATTACH;
                    srcPart.srfAttachNode.attachedPart = tgtPart;
                    srcPart.srfAttachNode.attachedPartId = tgtPart.flightID;
                }
                else
                {
                    if (srcAttachNode != null)
                    {
                        Debug.LogFormat("Attach type : {0} | ID : {1}",
                                        srcPart.srfAttachNode.nodeType, srcAttachNode.id);
                        srcPart.attachMode = AttachModes.STACK;
                        srcAttachNode.attachedPart = tgtPart;
                        srcAttachNode.attachedPartId = tgtPart.flightID;
                        if (tgtAttachNode != null)
                        {
                            tgtAttachNode.attachedPart = srcPart;
                        }
                        else
                        {
                            Debug.LogWarning("Target node is null");
                        }
                    }
                    else
                    {
                        Debug.LogErrorFormat("Source attach node not found: {0}", srcAttachNode.id);
                    }
                }
            }
            else
            {
                Debug.LogWarning("Missing source attach node !");
            }
            Debug.LogFormat(
                "Couple {0} with {1}", srcPart.name, tgtPart.name);
            srcPart.Couple(tgtPart);
        }
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
        }

        public JointTracker(AttachNode parentNode, AttachNode childNode, bool link = true)
        {
            this.nodes = new List<AttachNode> { parentNode, childNode };
            this.parts = new List<Part> { parentNode.owner, childNode.owner };
            log = new Logger("ReCoupler: JointTracker: " + parts[0].name + " and " + parts[1].name);
            if (link)
                this.createLink();
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

            newJoint.breakForce = Math.Min(parentPart.breakingForce, childPart.breakingForce);
            newJoint.breakTorque = Math.Min(parentPart.breakingTorque, childPart.breakingTorque);

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

            return this.joint;
        }

        public void destroyLink()
        {
            if (joint != null)
                GameObject.Destroy(joint);
        }
    }
}
