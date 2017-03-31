using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReCoupler
{
    public abstract class AbstractJointTracker
    {
        public List<Part> parts;
        public List<AttachNode> nodes;
        protected bool[] structNodeMan = new bool[2] { false, false };

        public AbstractJointTracker(AttachNode parentNode, AttachNode childNode)
        {
            this.nodes = new List<AttachNode> { parentNode, childNode };
            this.parts = new List<Part> { nodes[0].owner, nodes[1].owner };
        }

        public virtual void SetNodes()
        {
            nodes[0].attachedPart = parts[1];
            nodes[1].attachedPart = parts[0];
        }

        public virtual void Destroy()
        {
            if (nodes[0] != null)
                nodes[0].attachedPart = null;
            if (nodes[1] != null)
                nodes[1].attachedPart = null;
        }

        protected static bool SetModuleStructuralNode(AttachNode node)
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

        protected static void UnsetModuleStructuralNode(AttachNode node, bool structNodeMan)
        {
            if (node == null)
                return;
            ModuleStructuralNode structuralNode = node.owner.FindModulesImplementing<ModuleStructuralNode>().FirstOrDefault(msn => msn.attachNodeNames.Equals(node.id));
            if (structuralNode != null)
            {
                structuralNode.DespawnStructure();
                structuralNode.spawnManually = structNodeMan;
            }
        }
    }
}
