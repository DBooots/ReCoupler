using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReCoupler
{
    public class ModuleAttachNodeHide : PartModule
    {
        public AttachNode node = null;
        public bool isHidden
        {
            get
            {
                return _isHidden;
            }
        }
        
        private bool _isHidden = false;
        private bool awake = false;
        
        public float radius;
        protected AttachNode.NodeType nodeType;

        Logger log = new Logger("ModuleAttachNodeHide: ");

        public override void OnStart(StartState state)
        {
            log = new Logger("ModuleAttachNodeHide: " + part.name + ": ");
            log.debug("OnStart()");
        }

        public override void OnAwake()
        {
            awake = true;
        }

        public void OnDestroy()
        {
            if (_isHidden)
            {
                node.nodeType = this.nodeType;
                node.radius = this.radius;
            }
        }

        public void wasDuplicated()
        {
            this.node = part.FindAttachNode(node.id);
        }

        public void EditorAwake()
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;
            if (!awake)
            {
                awake = true;
                base.Awake();
            }
            //else
                //log.warning("Was already awake!");
        }

        public bool nodeFree()
        {
            bool free = true; ;
            foreach(ModuleAttachNodeHide hidingModule in part.FindModulesImplementing<ModuleAttachNodeHide>())
            {
                if (hidingModule.node == this.node)
                {
                    free = false;
                    break;
                }
            }
            return free;
        }

        public void hide()
        {
            if (!_isHidden)
            {
                this.radius = node.radius;
                this.nodeType = node.nodeType;

                node.nodeType = AttachNode.NodeType.Dock;
                node.radius = 0.001f;

                _isHidden = true;
            }
            else
                log.debug("Node was already hidden.");
        }

        public void show()
        {
            if (_isHidden || !_isHidden)
            {
                node.nodeType = this.nodeType;
                node.radius = this.radius;

                _isHidden = false;
            }
            else
                log.debug("Node was not hidden.");
        }

        public void setNode(AttachNode node)
        {
            if(_isHidden && this.node != null)
            {
                this.show();
            }
            if (!nodeFree())
            {
                log.error("That attach node was already claimed by another Module.");
                this.node = null;
            }
            if (part.attachNodes.Contains(node))
                this.node = node;
            else
            {
                this.node = null;
                log.error("That attach node isn't on this part.");
            }
        }
    }
}
