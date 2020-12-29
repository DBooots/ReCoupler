using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ReCoupler
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class ReCouplerEvents : MonoBehaviour
    {
        public void Awake()
        {
            Debug.Log("ReCoupler: Registering custom events.");
            ReCouplerUtils.onReCouplerJointFormed = new EventData<GameEvents.HostedFromToAction<Vessel, Part>>("onReCouplerJointFormed");
            ReCouplerUtils.onReCouplerJointBroken = new EventData<GameEvents.HostedFromToAction<Vessel, Part>>("onReCouplerJointBroken");
            ReCouplerUtils.onReCouplerEditorJointFormed = new EventData<GameEvents.FromToAction<Part, Part>>("onReCouplerEditorJointFormed");
            ReCouplerUtils.onReCouplerEditorJointBroken = new EventData<GameEvents.FromToAction<Part, Part>>("onReCouplerEditorJointBroken");
        }
    }

    public static class ReCouplerUtils
    {
        public static EventData<GameEvents.HostedFromToAction<Vessel, Part>> onReCouplerJointFormed;
        public static EventData<GameEvents.HostedFromToAction<Vessel, Part>> onReCouplerJointBroken;
        public static EventData<GameEvents.FromToAction<Part, Part>> onReCouplerEditorJointFormed;
        public static EventData<GameEvents.FromToAction<Part, Part>> onReCouplerEditorJointBroken;

        static Logger log = new Logger("ReCoupler: ReCouplerUtils: ");

        public enum JointType
        {
            EditorJointTracker,
            FlightJointTracker
        }

        public static List<TBase> CastList<TBase, TDerived>(this IList<TDerived> c) where TDerived : class, TBase
        {
            int count = c.Count;
            List<TBase> value = new List<TBase>(count);
            for (int i = 0; i < count; i++)
            {
                value.Add((TBase)c[i]);
            }
            return value;
        }

        public static IEnumerable<FlightReCoupler.FlightJointTracker> Generate_Flight(Vessel vessel, List<AttachNode> openNodes = null)
        {
            if (openNodes == null)
                openNodes = new List<AttachNode>();
            if (!ReCouplerSettings.settingsLoaded)
                ReCouplerSettings.LoadSettings();

            List<FlightReCoupler.FlightJointTracker> joints = new List<FlightReCoupler.FlightJointTracker>();
            foreach (AbstractJointTracker newJoint in checkPartsList(vessel.parts, openNodes, JointType.FlightJointTracker))
            {
                yield return (FlightReCoupler.FlightJointTracker)newJoint;
            }
        }

        public static IEnumerable<EditorReCoupler.EditorJointTracker> Generate_Editor(ShipConstruct vessel, List<AttachNode> openNodes = null)
        {
            if (openNodes == null)
                openNodes = new List<AttachNode>();
            if (!ReCouplerSettings.settingsLoaded)
                ReCouplerSettings.LoadSettings();

            List<EditorReCoupler.EditorJointTracker> joints = new List<EditorReCoupler.EditorJointTracker>();
            foreach (AbstractJointTracker newJoint in checkPartsList(vessel.parts, openNodes, JointType.EditorJointTracker))
            {
                yield return (EditorReCoupler.EditorJointTracker)newJoint;
            }
        }

        internal static IEnumerable<AbstractJointTracker> checkPartsList(List<Part> parts, List<AttachNode> openNodes, JointType jointType)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                foreach (AbstractJointTracker newJoint in checkPartNodes(parts[i], openNodes, jointType, false))
                    yield return newJoint;
            }
        }

        internal static IEnumerable<AbstractJointTracker> checkPartNodes(Part part, List<AttachNode> openNodes, JointType jointType, bool recursive = false)
        {
            List<AttachNode> partNodes = findOpenNodes(part);

            if (recursive)
            {
                Part[] children = part.FindChildParts<Part>(true);
                partNodes.AddRange(findOpenNodes(children));
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

                AttachNode closestNode = getEligiblePairing(partNodes[i], openNodes, ReCouplerSettings.connectRadius, ReCouplerSettings.connectAngle, ReCouplerSettings.allowRoboJoints, ReCouplerSettings.allowKASJoints);
                if (closestNode != null)
                {
                    if (jointType == JointType.EditorJointTracker)
                        yield return new EditorReCoupler.EditorJointTracker(partNodes[i], closestNode);
                    else if (jointType == JointType.FlightJointTracker)
                        yield return new FlightReCoupler.FlightJointTracker(partNodes[i], closestNode);
                    openNodes.Remove(closestNode);

                    //ReCouplerManager.combineCrossfeedSets(closestNode.owner, partNodes[i].owner);
                }
                else
                    partNodesToAdd.Add(partNodes[i]);
            }
            openNodes.AddRange(partNodesToAdd);
        }

        public static List<AttachNode> findReCoupledNodes(Part part)
        {
            List<AttachNode> problemNodes = new List<AttachNode>();
            List<Part> childs = part.FindChildParts<Part>(false).ToList();
            if (part.parent != null)
                childs.Add(part.parent);
            for (int i = 0; i < part.attachNodes.Count; i++)
            {
                if (part.attachNodes[i].attachedPart != null && !childs.Contains(part.attachNodes[i].attachedPart))
                    problemNodes.Add(part.attachNodes[i]);
            }
            return problemNodes;
        }

        public static List<AttachNode> findProblemNodes(Part part)
        {
            List<AttachNode> problemNodes = new List<AttachNode>();
            List<Part> childs = part.FindChildParts<Part>(false).ToList();
            if (part.parent != null)
                childs.Add(part.parent);
            for (int i = 0; i < part.attachNodes.Count; i++)
            {
                if (part.attachNodes[i].attachedPart != null && !childs.Contains(part.attachNodes[i].attachedPart))
                    problemNodes.Add(part.attachNodes[i]);
            }
            return problemNodes;
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
                    bool doNotJoin = false;

                    if (part.FindModulesImplementing<ModuleDecouple>().Any(decoupler => decoupler.isDecoupled && (decoupler.ExplosiveNode == part.attachNodes[i] || decoupler.isOmniDecoupler)))
                        doNotJoin = true;
                    /*foreach (ModuleDecouple decoupler in part.FindModulesImplementing<ModuleDecouple>())
                    {
                        if (decoupler.isDecoupled && (part.attachNodes[i] == decoupler.ExplosiveNode || decoupler.isOmniDecoupler))
                        {
                            doNotJoint = true;
                            break;
                        }
                    }*/
                    foreach (ModuleDockingNode dockingNode in part.FindModulesImplementing<ModuleDockingNode>())
                    {
                        if (dockingNode.referenceNode == null)
                        {
                            log.error("Docking node has null referenceNode! " + part.name);
                            continue;
                        }
                        if (dockingNode.referenceNode == part.attachNodes[i])
                        {
                            if (dockingNode.otherNode != null)
                                doNotJoin = true;
                            break;
                        }
                    }

                    /*foreach (ModuleCargoBay cargoBay in part.FindModulesImplementing<ModuleCargoBay>())
                    {
                        if (cargoBay.nodeInnerForeID == part.attachNodes[i].id || cargoBay.nodeInnerAftID == part.attachNodes[i].id)
                        {
                            doNotJoint = true;
                            break;
                        }
                    }*/
                    if (part.FindModulesImplementing<ModuleCargoBay>().Any(cargoBay => cargoBay.nodeInnerForeID == part.attachNodes[i].id || cargoBay.nodeInnerAftID == part.attachNodes[i].id))
                        doNotJoin = true;

                    if (part.Modules.Contains("ModuleIRServo"))
                        doNotJoin = true;
                    if (part.Modules.Contains("MuMechToggle"))
                        doNotJoin = true;

                    if (doNotJoin)
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

        public static AttachNode getEligiblePairing(AttachNode node, List<AttachNode> checkNodes, float radius = ReCouplerSettings.connectRadius_default, float angle = ReCouplerSettings.connectAngle_default, bool allowRoboJoints = ReCouplerSettings.allowRoboJoints_default, bool allowKASJoints = ReCouplerSettings.allowKASJoints_default)
        {
            float closestDist = radius;
            AttachNode closestNode = null;

            for (int j = 0; j < checkNodes.Count; j++)
            {
                if (node.owner == checkNodes[j].owner)
                    continue; // Nodes on the same Part are not eligible.
                if (node.owner.parent == checkNodes[j].owner || checkNodes[j].owner.parent == node.owner)
                    continue; // Parent-child relationships don't need doubling up.
                if (ReCouplerGUI.Instance.partPairsToIgnore.Any((Part[] parts) => parts.Contains(node.owner) && parts.Contains(checkNodes[j].owner)))
                    continue; // This one was told to be ignored.
                if (HighLogic.LoadedSceneIsEditor && EditorReCoupler.Instance != null)
                {
                    if (EditorReCoupler.Instance.hiddenNodes.Any(EJT => EJT.parts.Contains(node.owner) && EJT.parts.Contains(checkNodes[j].owner)))
                        continue;
                }
                else if (HighLogic.LoadedSceneIsFlight && FlightReCoupler.Instance != null)
                {
                    if (FlightReCoupler.Instance.trackedJoints.ContainsKey(node.owner.vessel))
                    {
                        if (FlightReCoupler.Instance.trackedJoints[node.owner.vessel].Any(FJT => FJT.parts.Contains(node.owner) && FJT.parts.Contains(checkNodes[j].owner)))
                            continue;
                    }
                }

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
                    if (TreeHasKinks(checkNodes[j].owner, node.owner, allowRoboJoints, allowKASJoints))
                        continue;

                    log.debug(node.owner.name + "/" + checkNodes[j].owner.name + ": " + dist + "m, at " + angleBtwn + " deg.");
                    closestNode = checkNodes[j];
                }
            }

            return closestNode;
        }

        public static bool TreeHasKinks(Part part1, Part part2, bool allowRoboJoints, bool allowKASJoints)
        {
            try
            {
                int partsBetween = 0;
                Part branchPart = FindCommonAncestor(part1, part2, out partsBetween);

                // Populate Parts list
                List<Part> partsToCheck = new List<Part>(partsBetween);
                Part addPart;
                if (branchPart != part1)
                {
                    addPart = part1;
                    do
                    {
                        addPart = addPart.parent;
                        partsToCheck.Add(addPart);
                    } while (addPart != branchPart);
                }
                if (branchPart != part2)
                {
                    addPart = part2;
                    do
                    {
                        addPart = addPart.parent;
                        partsToCheck.Add(addPart);
                    } while (addPart != branchPart);
                }
                partsToCheck.Remove(branchPart);

                for (int i = partsToCheck.Count - 1; i >= 0; i--)
                {
                    if (PartIsInvalidForPath(partsToCheck[i], allowRoboJoints, allowKASJoints))
                    {
                        log.debug("Part: " + partsToCheck[i].name + " had an ineligible module.");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                log.error("Encountered " + ex);
                return true;
            }
        }

        public static Part FindCommonAncestor(Part part1, Part part2, out int partsBetween)
        {
            Part rootPart1, rootPart2;
            int lvlPart1 = FindPartLevel(part1, out rootPart1);
            int lvlPart2 = FindPartLevel(part2, out rootPart2);
            partsBetween = 0;
            if (rootPart1 != rootPart2)
            {
                log.error("Parts are not part of the same tree.");
                return null;
            }

            Part lowerPart, higherPart;
            int lowerLvl, higherLvl;
            if (lvlPart1 <= lvlPart2)
            {
                lowerPart = part2;
                higherPart = part1;
                lowerLvl = lvlPart2;
                higherLvl = lvlPart1;
            }
            else
            {
                lowerPart = part1;
                higherPart = part2;
                lowerLvl = lvlPart1;
                higherLvl = lvlPart2;
            }
            for (int lvl = lowerLvl; lvl > higherLvl; lvl--)
            {
                lowerPart = lowerPart.parent;
                partsBetween += 1;
            }
            while (lowerPart != higherPart)
            {
                lowerPart = lowerPart.parent;
                higherPart = higherPart.parent;
                partsBetween += 2;
            }
            return lowerPart;
        }

        public static int FindPartLevel(Part part)
        {
            int lvl = 0;
            Part checkPart = part;
            while (checkPart.parent != null)
            {
                lvl += 1;
                checkPart = checkPart.parent;
            }
            return lvl;
        }

        public static int FindPartLevel(Part part, out Part rootPart)
        {
            int lvl = 0;
            Part checkPart = part;
            while (checkPart.parent != null)
            {
                lvl += 1;
                checkPart = checkPart.parent;
            }
            rootPart = checkPart;
            return lvl;
        }

        public static bool PartIsInvalidForPath(Part part, bool allowRoboJoints, bool allowKASJoints)
        {
            // Accounts for some Infernal Robotics Servos. (The mod is being revitalized currently and modern versions will likely comply with IJointLockState, handled below.
            if (!allowRoboJoints && (part.Modules.Contains("ModuleIRServo") || part.Modules.Contains("ServoMotor") || part.Modules.Contains("Servo")))
                return true;
            if (!allowRoboJoints && (part.Modules.Contains("MuMechToggle")))
                return true;
            // Accounts for all current joints in Kerbal Attachment System.
            if (!allowKASJoints && isKASPart(part))
                return true;
            // Accounts for ModuleGrappleNode and all of the Breaking Ground joints and servos.
            if (!allowRoboJoints && !isKASPart(part) && (part.FindModuleImplementing<IJointLockState>() != null))
                return true;
            return false;
        }

        private static bool isKASPart(Part part)
        {
            return (part.Modules.Contains("AbstractJoint") || part.Modules.Contains("KASJointCableBase") || part.Modules.Contains("KASJointTwoEndsSphere") || part.Modules.Contains("KASJointTowBar") || part.Modules.Contains("KASJointRigid"));
        }

        // Adapted from KASv1 by IgorZ
        // See https://github.com/ihsoft/KAS/tree/KAS-v1.0
        // https://github.com/ihsoft/KAS/blob/KAS-v1.0/Source/api_impl/LinkUtilsImpl.cs#L50-L82
        // This method is in the public domain: https://github.com/ihsoft/KAS/blob/KAS-v1.0/LICENSE-1.0.md
        public static void CoupleParts(AttachNode sourceNode, AttachNode targetNode)
        {
            Part srcPart = sourceNode.owner;
            Part trgPart = targetNode.owner;
            CoupleParts(srcPart, trgPart, sourceNode, targetNode);
        }

        public static void CoupleParts(Part srcPart, Part trgPart, AttachNode sourceNode, AttachNode targetNode = null)
        {
            Vessel srcVessel = srcPart.vessel;
            Vessel trgVessel = trgPart.vessel;

            DockedVesselInfo vesselInfo = new DockedVesselInfo();
            vesselInfo.name = srcVessel.vesselName;
            vesselInfo.vesselType = srcVessel.vesselType;
            vesselInfo.rootPartUId = srcVessel.rootPart.flightID;

            GameEvents.onActiveJointNeedUpdate.Fire(srcVessel);
            GameEvents.onActiveJointNeedUpdate.Fire(trgVessel);
            sourceNode.attachedPart = trgPart;
            sourceNode.attachedPartId = trgPart.flightID;
            if (sourceNode.id != "srfAttach")
            {
                srcPart.attachMode = AttachModes.STACK;
                if (targetNode != null)
                    targetNode.attachedPart = srcPart;
                else
                    log.warning("Target node is null.");
            }
            else
            {
                srcPart.attachMode = AttachModes.SRF_ATTACH;
            }
            srcPart.Couple(trgPart);
            // Depending on how active vessel has updated do either force active or make active. Note, that
            // active vessel can be EVA kerbal, in which case nothing needs to be adjusted.    
            // FYI: This logic was taken from ModuleDockingNode.DockToVessel.
            if (srcVessel == FlightGlobals.ActiveVessel)
            {
                FlightGlobals.ForceSetActiveVessel(sourceNode.owner.vessel);  // Use actual vessel.
            }
            else if (sourceNode.owner.vessel == FlightGlobals.ActiveVessel)
            {
                sourceNode.owner.vessel.MakeActive();
            }
            GameEvents.onVesselWasModified.Fire(sourceNode.owner.vessel);

            ModuleDockingNode sourcePort, targetPort;
            if (hasDockingPort(sourceNode, out sourcePort))
                CoupleDockingPortWithPart(sourcePort);
            if (hasDockingPort(targetNode, out targetPort))
                CoupleDockingPortWithPart(targetPort);
        }

        // Adapted from KIS by IgorZ
        // See https://github.com/ihsoft/KIS/blob/master/Source/KIS_Shared.cs#L1005-L1039
        // This method is in the public domain.
        /// <summary>Couples docking port with a part at its reference attach node.</summary>
        /// <remarks>Both parts must be already connected and the attach nodes correctly set.</remarks>
        /// <param name="dockingNode">Port to couple.</param>
        /// <returns><c>true</c> if coupling was successful.</returns>
        public static bool CoupleDockingPortWithPart(ModuleDockingNode dockingNode)
        {
            Part tgtPart = dockingNode.referenceNode.attachedPart;
            if (tgtPart == null)
            {
                log.error("Node's part " + dockingNode.part.name + " is not attached to anything thru the reference node");
                return false;
            }
            if (dockingNode.state != dockingNode.st_ready.name)
            {
                log.debug("Hard reset docking node " + dockingNode.part.name + " from state " + dockingNode.state + " to " + dockingNode.st_ready.name);
                dockingNode.dockedPartUId = 0;
                dockingNode.dockingNodeModuleIndex = 0;
                // Target part lived in real world for some time, so its state may be anything.
                // Do a hard reset.
                dockingNode.fsm.StartFSM(dockingNode.st_ready.name);
            }
            var initState = dockingNode.lateFSMStart(PartModule.StartState.None);
            // Make sure part init catched the new state.
            while (initState.MoveNext())
            {
                // Do nothing. Just wait.
            }
            if (dockingNode.fsm.currentStateName != dockingNode.st_preattached.name)
            {
                log.warning("Node on " + dockingNode.part.name + " is unexpected state " + dockingNode.fsm.currentStateName);
                return false;
            }
            log.debug("Successfully set docking node " + dockingNode.part + " to state " + dockingNode.fsm.currentStateName + " with part " + tgtPart.name);
            return true;
        }

        public static bool hasDockingPort(AttachNode node, out ModuleDockingNode dockingPort)
        {
            dockingPort = node.owner.FindModulesImplementing<ModuleDockingNode>().FirstOrDefault(dockingNode => dockingNode.referenceNode == node);
            return (dockingPort != null);
        }
    }
}
