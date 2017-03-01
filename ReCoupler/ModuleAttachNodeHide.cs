using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReCoupler
{
    class ModuleAttachNodeHide : PartModule
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

        protected float radius;
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
                this.show();
        }

        public void Create(AttachNode node)
        {
            if (!awake)
            {
                awake = true;
                base.Awake();
                this.node = node;
            }
            else
                log.warning("Was already awake!");
        }

        public void hide()
        {
            if (!_isHidden)
            {
                this.radius = node.radius;
                this.nodeType = node.nodeType;

                node.nodeType = AttachNode.NodeType.Dock;
                node.radius = 0.001f;
            }
            else
                log.debug("Node was already hidden.");
        }

        public void show()
        {
            if (_isHidden)
            {
                node.nodeType = this.nodeType;
                node.radius = this.radius;
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
            this.node = node;
        }
    }
}
