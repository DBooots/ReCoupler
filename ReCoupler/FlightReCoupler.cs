using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Collections.ObjectModel;   // Does not work in .NET 3.5 for ObservableCollection.
//Using my own implementation instead.

namespace ReCoupler
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class FlightReCoupler : MonoBehaviour
    {
        public static FlightReCoupler Instance;

        internal static Logger log = new Logger("ReCoupler: FlightReCoupler: ");

        public Dictionary<Vessel, ObservableCollection<FlightJointTracker>> trackedJoints = new Dictionary<Vessel, ObservableCollection<FlightJointTracker>>();
        public List<FlightJointTracker> allJoints
        {
            get
            {
                if (_dictChanged)
                {
                    _allJoints.Clear();
                    foreach (IList<FlightJointTracker> joints in trackedJoints.Values)
                    {
                        _allJoints.AddRange(joints);
                    }
                    _dictChanged = false;
                }
                return _allJoints;
            }
        }
        private List<FlightJointTracker> _allJoints = new List<FlightJointTracker>();
        private bool _dictChanged = false;
        private Dictionary<int, Coroutine> delayedUpdates = new Dictionary<int, Coroutine>();
        
        public void Awake()
        {
            if (Instance)
                Destroy(Instance);
            Instance = this;
            
            GameEvents.onVesselGoOffRails.Add(onVesselGoOffRails);
            GameEvents.onVesselCreate.Add(onVesselCreate);
            GameEvents.onVesselPartCountChanged.Add(onVesselPartCountChanged);
            GameEvents.onJointBreak.Add(onJointBreak);
            GameEvents.onVesselDestroy.Add(onVesselDestroy);
            GameEvents.onPartDie.Add(onPartDie);

        }

        public void OnDestroy()
        {
            GameEvents.onVesselGoOffRails.Remove(onVesselGoOffRails);
            GameEvents.onVesselCreate.Remove(onVesselCreate);
            GameEvents.onVesselPartCountChanged.Remove(onVesselPartCountChanged);
            GameEvents.onJointBreak.Remove(onJointBreak);
            GameEvents.onVesselDestroy.Remove(onVesselDestroy);
            GameEvents.onPartDie.Remove(onPartDie);
        }

        private void onVesselDestroy(Vessel vessel)
        {
            if (!trackedJoints.ContainsKey(vessel))
                return;
            clearJoints(vessel);    // Also removes it from the dictionary.
        }

        private void onPartDie(Part part)
        {
            List<FlightJointTracker> jointsWithPart = allJoints.FindAll((FlightJointTracker jt) => jt.parts.Contains(part));
            for (int i = jointsWithPart.Count - 1; i >= 0; i--)
            {
                jointsWithPart[i].Destroy();
                if (trackedJoints.ContainsKey(part.vessel))
                    trackedJoints[part.vessel].Remove(jointsWithPart[i]);
                else
                    log.error("The destroyed part's vessel was not in the dictionary.");
            }
        }

        private void onVesselCreate(Vessel vessel)
        {
            if (!vessel.loaded || vessel.packed)
                return;
            if (!delayedUpdates.ContainsKey(Time.frameCount))
                delayedUpdates.Add(Time.frameCount, StartCoroutine(DelayedUpdate(Time.frameCount)));
        }

        private void onVesselPartCountChanged(Vessel vessel)
        {
            if (!vessel.loaded || vessel.packed)
                return;
            updateVessel(vessel);
            if (!delayedUpdates.ContainsKey(Time.frameCount))
                delayedUpdates.Add(Time.frameCount, StartCoroutine(DelayedUpdate(Time.frameCount)));
        }

        IEnumerator DelayedUpdate(int time)
        {
            yield return new WaitForFixedUpdate();
            log.debug("Running DelayedUpdate: " + Planetarium.GetUniversalTime());
            List<Vessel> vessels = trackedJoints.Keys.ToList();
            for (int i = vessels.Count - 1; i >= 0; i--)
            {
                updateVessel(vessels[i]);
                bool combinedAny = false;
                FlightJointTracker jt;
                for (int j = trackedJoints[vessels[i]].Count - 1; j >= 0; j--)
                {
                    jt = trackedJoints[vessels[i]][j];

                    if (jt.parts[0] == null || jt.parts[1] == null)
                    {
                        log.debug("Removing a null joint.");
                        jt.Destroy();
                        trackedJoints[vessels[i]].Remove(jt);
                        continue;
                    }
                    if (!jt.linkCreated)
                    {
                        log.debug("A joint must have broken.");
                        jt.Destroy();
                        trackedJoints[vessels[i]].Remove(jt);
                        continue;
                    }
                    else if (jt.isTrackingDockingPorts)
                    {
                        ModuleDockingNode fromNode, toNode;
                        ReCouplerUtils.hasDockingPort(jt.nodes[0], out fromNode);
                        ReCouplerUtils.hasDockingPort(jt.nodes[1], out toNode);
                        /*if (dockingNode.state != dockingNode.st_docked_dockee.name &&
                            dockingNode.state != dockingNode.st_docked_docker.name &&
                            dockingNode.state != dockingNode.st_docker_sameVessel.name &&
                            dockingNode.state != dockingNode.st_preattached.name)*/
                        if ((fromNode != null && fromNode.otherNode != toNode) && (toNode != null && toNode.otherNode != fromNode))
                        {
                            log.debug("A joint must have undocked.");
                            trackedJoints[vessels[i]].Remove(jt);
                            continue;
                        }
                    }

                    if (jt.parts[0].vessel != jt.parts[1].vessel)
                    {
                        log.debug("Vessel tree split, but should be re-combined.");
                        log.debug(jt.parts[0].name + " / " + jt.parts[1].name);
                        log.debug(jt.parts[0].vessel + " / " + jt.parts[1].vessel);
                        // The vessel split at the official tree, but isn't split at this joint.

                        if (jt.Couple(ownerVessel: vessels[i]))
                        {
                            log.debug("Removing joint since it is now a real, coupled joint.");
                            log.debug("Parent0: " + jt.parts[0].parent.name + " Parent1: " + jt.parts[1].parent.name);
                            trackedJoints[vessels[i]].Remove(jt);
                        }
                        else
                            log.error("Could not couple parts!");

                        combinedAny = true;
                        continue;
                    }
                    else if (jt.parts[0].vessel != vessels[i] && jt.parts[1].vessel != vessels[i])
                    {
                        log.debug("Removing joint between " + jt.parts[0].name + " and " + jt.parts[1].name + " because they are no longer part of this vessel.");
                        if (jt.parts[0].vessel == jt.parts[1].vessel)
                        {
                            log.debug("Adding them to vessel " + jt.parts[0].vessel.vesselName + " instead.");
                            if (!trackedJoints.ContainsKey(jt.parts[0].vessel))
                                addNewVesselToDict(jt.parts[0].vessel);
                            trackedJoints[jt.parts[0].vessel].Add(jt);
                            jt.combineCrossfeedSets();
                        }
                        else
                            jt.Destroy();
                        trackedJoints[vessels[i]].Remove(jt);
                        continue;
                    }
                }

                if (combinedAny)
                {
                    for (int j = trackedJoints[vessels[i]].Count - 1; j >= 0; j--)
                    {
                        trackedJoints[vessels[i]][j].combineCrossfeedSets();
                    }
                }
            }
            checkActiveVessels();
            delayedUpdates.Remove(time);
        }

        private void onVesselGoOffRails(Vessel vessel) { log.debug("onVesselGoOffRails: " + vessel.vesselName); checkActiveVessels(); }

        private void onJointBreak(EventReport data)
        {
            log.debug("onJointBreak: " + data.origin.name + " on " + data.origin.vessel.vesselName);
            Part brokenPart = data.origin;
            if (!trackedJoints.ContainsKey(brokenPart.vessel))
                return;
            IList<FlightJointTracker> joints = trackedJoints[brokenPart.vessel];
            for (int i = joints.Count - 1; i >= 0; i--)
            {
                if (joints[i].parts.Contains(brokenPart))
                {
                    if (!joints[i].linkCreated)
                    {
                        joints[i].Destroy();
                        trackedJoints[brokenPart.vessel].Remove(joints[i]);
                    }
                    else
                    {
                        this.StartCoroutine(DelayedBreak(joints[i], brokenPart.vessel));
                    }
                }
            }
        }

        IEnumerator DelayedBreak(FlightJointTracker joint, Vessel owner)
        {
            yield return new WaitForFixedUpdate();
            log.debug("Running DelayedBreak on " + owner.vesselName);
            if (!joint.linkCreated)
            {
                joint.Destroy();
                trackedJoints[owner].Remove(joint);
            }
        }

        public void updateVessel(Vessel vessel)
        {
            if (!vessel.loaded || vessel.packed)
                return;
            if (!trackedJoints.ContainsKey(vessel))
                addNewVesselToDict(vessel);
            IList<FlightJointTracker> joints = trackedJoints[vessel];

            for (int j = joints.Count - 1; j >= 0; j--)
            {
                if (joints[j].decouplers.Any(d => d.isDecoupled))
                {
                    log.debug("Decoupler " + joints[j].decouplers.First(d => d.isDecoupled).part.name + " decoupled. Removing from joints list.");
                    joints[j].Destroy();
                    this.StartCoroutine(DelayedDecouplerCheck(joints[j], vessel));
                    joints.RemoveAt(j);
                    continue;
                }
            }
        }

        IEnumerator DelayedDecouplerCheck(FlightJointTracker joint, Vessel owner)
        {
            FixedJoint[] tempJoints = new FixedJoint[2];
            if (joint.ParentStillValid(0))
            {
                tempJoints[0] = joint.parts[0].gameObject.AddComponent<FixedJoint>();
                tempJoints[0].connectedBody = joint.parents[0].part.Rigidbody;
            }
            if (joint.ParentStillValid(1))
            {
                tempJoints[1] = joint.parts[1].gameObject.AddComponent<FixedJoint>();
                tempJoints[1].connectedBody = joint.parents[1].part.Rigidbody;
            }
            yield return new WaitForFixedUpdate();
            if (tempJoints[0] != null)
                DestroyImmediate(tempJoints[0]);
            if (tempJoints[1] != null)
                DestroyImmediate(tempJoints[1]);
            log.debug("Running DelayedDecouplerCheck on " + owner.vesselName);
            joint.PostDecouplerCheck();
        }

        public void checkActiveVessels()
        {
            List<Vessel> activeVessels = FlightGlobals.VesselsLoaded;
            List<Vessel> trackedVessels = trackedJoints.Keys.ToList();
            for (int i = trackedVessels.Count - 1; i >= 0; i--)
            {
                try
                {
                    for (int j = trackedJoints[trackedVessels[i]].Count - 1; j >= 0; j--)
                    {
                        if (trackedJoints[trackedVessels[i]][j].parts[0].vessel != trackedVessels[i])
                        {
                            if (trackedJoints.ContainsKey(trackedJoints[trackedVessels[i]][j].parts[0].vessel))
                            {
                                trackedJoints[trackedJoints[trackedVessels[i]][j].parts[0].vessel].Add(trackedJoints[trackedVessels[i]][j]);
                                trackedJoints[trackedVessels[i]].RemoveAt(j);
                            }
                            else
                                log.warning("The Vessel a JointTracker should belong to has not yet been added to the dictionary.");
                        }
                    }
                    if (!activeVessels.Contains(trackedVessels[i]))
                    {
                        log.debug("Vessel " + trackedVessels[i].name + " is no longer loaded. Removing it from the dictionary.");
                        onVesselDestroy(trackedVessels[i]);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    log.error(ex + " while checking loaded vessels.");
                }
            }
            for (int i = activeVessels.Count - 1; i >= 0; i--)
            {
                if (trackedJoints.ContainsKey(activeVessels[i]))
                    continue;
                generateJoints(activeVessels[i]);
            }
        }

        public void generateJoints(Vessel vessel)
        {
            log.debug("generateJoints: " + vessel.vesselName);
            if (!trackedJoints.ContainsKey(vessel))
                addNewVesselToDict(vessel);
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
                    if(activePart.attachNodes[n].attachedPart!=null && !childen.Contains(activePart.attachNodes[n].attachedPart))
                    {
                        parseAttachNodes(activePart.attachNodes[n], activePart.attachNodes[n].attachedPart.attachNodes.FindAll(AN => AN.attachedPart == activePart), vessel);
                    }
                }
            }
        }

        private void parseAttachNodes(AttachNode parentNode, List<AttachNode> childNodes, Vessel vessel = null)
        {
            if (vessel == null)
                vessel = parentNode.owner.vessel;
            for (int i = 0; i < childNodes.Count; i++)
            {
                FlightJointTracker existingTracker = allJoints.FirstOrDefault((FlightJointTracker jt) => jt.nodes.Contains(parentNode) && jt.nodes.Contains(childNodes[i]));
                if (existingTracker == null)
                    trackedJoints[vessel].Add(new FlightJointTracker(parentNode, childNodes[i]));
                else if (!trackedJoints[vessel].Contains(existingTracker))
                {
                    trackedJoints[vessel].Add(existingTracker);
                    Vessel currentListing = findDictEntry(existingTracker);
                    if (currentListing != null)
                        trackedJoints[currentListing].Remove(existingTracker);
                }
            }
        }

        public Vessel findDictEntry(FlightJointTracker jt)
        {
            for (int i = trackedJoints.Count - 1; i >= 0; i--)
            {
                if (trackedJoints.Values.ElementAt(i).Contains(jt))
                    return trackedJoints.Keys.ElementAt(i);
            }
            return null;
        }

        public void clearJoints(Vessel vessel)
        {
            if (!trackedJoints.ContainsKey(vessel))
            {
                log.warning(vessel.name + " was not in the dictionary.");
                return;
            }
            for (int i = trackedJoints[vessel].Count - 1; i >= 0; i--)
                trackedJoints[vessel][i].Destroy();
            trackedJoints.Remove(vessel);
            _dictChanged = true;
        }

        public void regenerateJoints(Vessel vessel)
        {
            if (trackedJoints.ContainsKey(vessel))
                clearJoints(vessel);
            addNewVesselToDict(vessel);
            foreach (FlightJointTracker joint in ReCouplerUtils.Generate_Flight(vessel))
            {
                trackedJoints[vessel].Add(joint);
            }
        }

        public void regenerateJoints()
        {
            List<Vessel> vessels = FlightGlobals.VesselsLoaded;
            for (int i = 0; i < vessels.Count; i++)
            {
                regenerateJoints(vessels[i]);
            }
        }

        public void addNewVesselToDict(Vessel vessel)
        {
            if (!trackedJoints.ContainsKey(vessel))
            {
                trackedJoints.Add(vessel, new ObservableCollection<FlightJointTracker>());
                _dictChanged = true;
                trackedJoints[vessel].CollectionChanged += FlightReCoupler_CollectionChanged;
            }
        }

        private void FlightReCoupler_CollectionChanged(object sender, EventArgs e)
        {
            _dictChanged = true;
        }

        public class FlightJointTracker : AbstractJointTracker
        {
            public struct Parent
            {
                public Part part;
                public AttachNode node;
                public AttachNode nodeTo;
                public Parent(Part part, AttachNode node, AttachNode nodeTo)
                {
                    this.part = part;
                    this.node = node;
                    this.nodeTo = nodeTo;
                }
            }
            public List<Parent> parents = new List<Parent>();
            public ConfigurableJoint joint = null;
            public List<PartSet> oldCrossfeedSets = new List<PartSet>(2);
            public bool linkCreated
            {
                get { return joint != null || _isTrackingDockingPorts; }
            }
            private bool _isTrackingDockingPorts = false;
            public bool isTrackingDockingPorts
            {
                get { return _isTrackingDockingPorts; }
            }

            Logger log;

            protected List<ModuleDecouple> cachedDecouplers = null;
            private uint[] oldIDs = new uint[2];

            public List<ModuleDecouple> decouplers
            {
                get
                {
                    if (cachedDecouplers == null)
                    {
                        cachedDecouplers = new List<ModuleDecouple>();
                        for (int i = 0; i < parts.Count; i++)
                        {
                            cachedDecouplers.AddRange(parts[i].FindModulesImplementing<ModuleDecouple>().FindAll(decoupler => decoupler.isOmniDecoupler || decoupler.ExplosiveNode == nodes[i]));
                        }
                    }
                    return cachedDecouplers;
                }
            }

            public FlightJointTracker(ConfigurableJoint joint, AttachNode parentNode, AttachNode childNode, bool link = true, bool isTrackingDockingPorts = false) : base(parentNode, childNode)
            {
                this.joint = joint;
                log = new Logger("ReCoupler: FlightJointTracker: " + parts[0].name + " and " + parts[1].name);
                this._isTrackingDockingPorts = isTrackingDockingPorts;
                this.CheckParents();
                if (link)
                    this.CreateLink();
                if (!isTrackingDockingPorts)
                    this.SetNodes();
            }

            public FlightJointTracker(AttachNode parentNode, AttachNode childNode, bool link = true, bool isTrackingDockingPorts = false) : base(parentNode, childNode)
            {
                log = new Logger("ReCoupler: FlightJointTracker: " + parts[0].name + " and " + parts[1].name + " ");
                this._isTrackingDockingPorts = isTrackingDockingPorts;
                this.CheckParents();
                if (link)
                    this.CreateLink();
                if (!isTrackingDockingPorts)
                    this.SetNodes();
            }

            public FlightJointTracker(AbstractJointTracker parent, bool link = true, bool isTrackingDockingPorts = false) : base(parent.nodes[0], parent.nodes[1])
            {
                log = new Logger("ReCoupler: FlightJointTracker: " + parts[0].name + " and " + parts[1].name + " ");
                this._isTrackingDockingPorts = isTrackingDockingPorts;
                this.CheckParents();
                if (link)
                    this.CreateLink();
                if (!isTrackingDockingPorts)
                    this.SetNodes();
            }

            public override void SetNodes()
            {
                oldIDs = new uint[] { nodes[0].attachedPartId, nodes[1].attachedPartId };
                base.SetNodes();
                nodes[0].attachedPartId = parts[1].flightID;
                nodes[1].attachedPartId = parts[0].flightID;

                this.structNodeMan[0] = SetModuleStructuralNode(nodes[0]);
                this.structNodeMan[1] = SetModuleStructuralNode(nodes[1]);

                GameEvents.onVesselWasModified.Fire(parts[0].vessel);
            }

            public ConfigurableJoint CreateLink()
            {
                if (this.joint != null)
                {
                    log.warning("This link already has a joint object.");
                    return this.joint;
                }
                if (_isTrackingDockingPorts)
                {
                    this.joint = new ConfigurableJoint();
                    return this.joint;
                }

                try
                {
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
                }
                catch (Exception ex)
                {
                    log.error("Could not create physical joint: " + ex);
                }
                this.SetNodes();

                ConnectedLivingSpacesCompatibility.RequestAddConnection(this.parts[0], this.parts[1]);
                ReCouplerUtils.onReCouplerJointFormed.Fire(new GameEvents.HostedFromToAction<Vessel, Part>(parts[0].vessel, parts[0], parts[1]));
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
                if (_isTrackingDockingPorts)
                    return;
                if (parts[0].crossfeedPartSet == null)
                    return;
                if (parts[0].crossfeedPartSet.ContainsPart(parts[1]))
                    return;
                oldCrossfeedSets.Add(parts[0].crossfeedPartSet);
                oldCrossfeedSets.Add(parts[1].crossfeedPartSet);
                log.debug("Part Xfeed: " + parts[0].fuelCrossFeed + " node: " + nodes[0].ResourceXFeed);
                log.debug("Part Xfeed: " + parts[1].fuelCrossFeed + " node: " + nodes[1].ResourceXFeed);
                log.debug("Combining Crossfeed Sets.");
                combineCrossfeedSets(this.parts[0], this.parts[1]);
            }

            public bool Couple(Vessel ownerVessel = null)
            {
                try
                {
                    int owner;
                    if (parts[1].vessel == FlightGlobals.ActiveVessel ||
                        (parts[0].vessel != FlightGlobals.ActiveVessel &&
                            (parts[1].vessel == ownerVessel ||
                            (parts[1].vessel.Parts.Count > parts[0].vessel.Parts.Count && parts[0].vessel != ownerVessel))))
                        owner = 1;
                    else
                        owner = 0;

                    ownerVessel = parts[owner].vessel;

                    AttachNode targetNode = owner == 0 ? nodes[0] : nodes[1];
                    AttachNode coupleNode = owner == 0 ? nodes[1] : nodes[0];
                    Part targetPart = owner == 0 ? parts[0] : parts[1];
                    Part couplePart = owner == 0 ? parts[1] : parts[0];

                    log.debug("Coupling " + couplePart.name + " to " + targetPart.name + ".");

                    if (ownerVessel == FlightGlobals.ActiveVessel)
                    {
                        // Save camera information here.
                    }
                    ReCouplerUtils.CoupleParts(coupleNode, targetNode);
                    if(ownerVessel == FlightGlobals.ActiveVessel)
                    {
                        // Restore camera information here
                    }

                    //targetPart.vessel.currentStage = KSP.UI.Screens.StageManager.RecalculateVesselStaging(targetPart.vessel);
                    ownerVessel.currentStage = KSP.UI.Screens.StageManager.RecalculateVesselStaging(ownerVessel) + 1;

                    if (!isTrackingDockingPorts)
                        this.Destroy();
                    return true;
                }
                catch (Exception ex)
                {
                    log.error("Error in coupling: " + ex);
                    return false;
                }
            }

            public override void Destroy()
            {
                if (_isTrackingDockingPorts)
                    return;

                log.debug("Destroying a link.");
                if (joint != null)
                    GameObject.Destroy(joint);

                base.Destroy();

                if (oldCrossfeedSets.Count > 0)
                {
                    parts[0].crossfeedPartSet = oldCrossfeedSets[0];
                    parts[1].crossfeedPartSet = oldCrossfeedSets[1];
                }

                UnsetModuleStructuralNode(nodes[0], structNodeMan[0]);
                UnsetModuleStructuralNode(nodes[1], structNodeMan[1]);

                if (this.parts[0] != null && this.parts[1] != null)
                    ConnectedLivingSpacesCompatibility.RequestRemoveConnection(this.parts[0], this.parts[1]);

                if (this.parts[0] != null)
                    ReCouplerUtils.onReCouplerJointBroken.Fire(new GameEvents.HostedFromToAction<Vessel, Part>(this.parts[0].vessel, this.parts[0], this.parts[1]));
                else if (this.parts[1] != null)
                    ReCouplerUtils.onReCouplerJointBroken.Fire(new GameEvents.HostedFromToAction<Vessel, Part>(this.parts[1].vessel, this.parts[0], this.parts[1]));
                else
                    ReCouplerUtils.onReCouplerJointBroken.Fire(new GameEvents.HostedFromToAction<Vessel, Part>(null, this.parts[0], this.parts[1]));

                if (parts[0] != null && parts[0].vessel != null)
                    GameEvents.onVesselWasModified.Fire(parts[0].vessel);
                else if (parts[1] != null && parts[1].vessel != null)
                    GameEvents.onVesselWasModified.Fire(parts[1].vessel);
            }

            private void CheckParents()
            {
                parents.Capacity = parts.Count;
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i].parent != null)
                    {
                        parents.Add(new Parent(parts[i].parent, parts[i].parent.FindAttachNodeByPart(parts[i]), parts[i].FindAttachNodeByPart(parts[i].parent)));
                    }
                }
            }

            public void PostDecouplerCheck()
            {
                for(int i = parts.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (parts[i].vessel != parents[i].part.vessel && ParentStillValid(parents[i]))
                        {
                            log.debug("Coupling part to parent.");
                            ReCouplerUtils.CoupleParts(parts[i], parents[i].part, parents[i].nodeTo, parents[i].node);
                        }
                    }
                    catch (Exception ex)
                    {
                        log.error("Error in PostDecouplerCheck: " + ex);
                    }
                }
            }

            public bool ParentStillValid(int i) { return ParentStillValid(parents[i]); }

            public static bool ParentStillValid(Parent parent)
            {
                if (parent.node == null)
                    return true;

                if (parent.part.FindModulesImplementing<ModuleDecouple>().Any(
                    decoupler => decoupler.isDecoupled && (decoupler.ExplosiveNode == parent.node || decoupler.isOmniDecoupler)))
                    return false;
                if (parent.nodeTo.owner.FindModulesImplementing<ModuleDecouple>().Any(
                    decoupler => decoupler.isDecoupled && (decoupler.ExplosiveNode == parent.nodeTo || decoupler.isOmniDecoupler)))
                    return false;
                    
                // TODO:
                //if (parent.part.FindModulesImplementing<ModuleDockingNode>().Any(dockingNode => dockingNode.referenceNode == parent.node))
                //    return false;
                return true;
            }
        }
    }
}
