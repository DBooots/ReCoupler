using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ReCoupler
{
    public static class ConnectedLivingSpacesCompatibility
    {
        internal static bool clsChecked = false;
        private static bool _isClsInstalled = false;
        private static bool _clsChecked = false;
        private static Assembly assembly = null;
        private static Type CLSAddonType = null;
        private static object CLSAddonInstance = null;
        private static MethodInfo requestAddConnectionMethod = null;
        private static MethodInfo requestAddConnectionsMethod = null;
        private static MethodInfo requestRemoveConnectionMethod = null;
        private static MethodInfo requestRemoveConnectionsMethod = null;
        private static PropertyInfo InstanceProperty = null;

        private static Logger log = new Logger("CLSCompatibility: ");

        public static bool IsCLSInstalled
        {
            get
            {
                if (!_clsChecked)
                {
                    try
                    {
                        string assemblyName = "ConnectedLivingSpace";
                        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                        assembly = (from a in assemblies
                                    where a.FullName.Contains(assemblyName)
                                    select a).SingleOrDefault();
                        log.debug(assembly != null ? assembly.FullName : "ConnectedLivingSpace was not found.");
                        if (assembly != null)
                        {
                            _isClsInstalled = true;
                            CLSAddonType = assembly.GetType("ConnectedLivingSpace.CLSAddon");

                            InstanceProperty = CLSAddonType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

                            requestAddConnectionMethod = CLSAddonType.GetMethod("RequestAddConnection");
                            requestAddConnectionsMethod = CLSAddonType.GetMethod("RequestAddConnections");

                            requestRemoveConnectionMethod = CLSAddonType.GetMethod("RequestRemoveConnection");
                            requestRemoveConnectionsMethod = CLSAddonType.GetMethod("RequestRemoveConnections");

                            if (requestAddConnectionMethod == null || requestAddConnectionsMethod == null || requestRemoveConnectionMethod == null || requestRemoveConnectionsMethod == null || InstanceProperty == null)
                            {
                                _isClsInstalled = false;
                                log.debug("One of the required methods was not found. You may be using an outdated CLS version.");
                            }
                        }
                        else
                            _isClsInstalled = false;
                    }
                    catch
                    {
                        log.error("Encountered an exception when detecting CLS installation.");
                        _isClsInstalled = false;
                    }
                }
                _clsChecked = true;
                return _isClsInstalled;
            }
        }

        public static bool GetCLSAddonInstance()
        {
            if (IsCLSInstalled)
                CLSAddonInstance = InstanceProperty.GetValue(null, null);
            return CLSAddonInstance != null;
        }

        public static void RequestAddConnection(Part part1, Part part2)
        {
            if (IsCLSInstalled && GetCLSAddonInstance())
            {
                requestAddConnectionMethod.Invoke(CLSAddonInstance, new object[] { part1, part2 });
            }
        }
        public static void RequestAddConnections(List<Part> part1, List<Part> part2)
        {
            if (IsCLSInstalled && GetCLSAddonInstance())
            {
                requestAddConnectionsMethod.Invoke(CLSAddonInstance, new object[] { part1, part2 });
            }
        }
        public static void RequestRemoveConnection(Part part1, Part part2)
        {
            if (IsCLSInstalled && GetCLSAddonInstance())
            {
                requestRemoveConnectionMethod.Invoke(CLSAddonInstance, new object[] { part1, part2 });
            }
        }
        public static void RequestRemoveConnections(List<Part> part1, List<Part> part2)
        {
            if (IsCLSInstalled && GetCLSAddonInstance())
            {
                requestRemoveConnectionsMethod.Invoke(CLSAddonInstance, new object[] { part1, part2 });
            }
        }
    }
}
