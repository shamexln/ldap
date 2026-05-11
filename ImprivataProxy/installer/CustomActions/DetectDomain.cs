using System;
using System.Diagnostics;
using System.Linq;
using WixToolset.Dtf.WindowsInstaller;

namespace ImprivataProxy.Installer.CustomActions
{
    public class DetectDomain
    {
        [CustomAction]
        public static ActionResult DetectDomainInfo(Session session)
        {
            try
            {
                string domain = Environment.GetEnvironmentVariable("USERDNSDOMAIN");
                if (string.IsNullOrEmpty(domain))
                {
                    domain = session["COMPUTER_DOMAIN"];
                }

                if (string.IsNullOrEmpty(domain))
                {
                    session.Log("DetectDomain: machine is not domain-joined, skipping.");
                    return ActionResult.Success;
                }

                session.Log("DetectDomain: detected domain = " + domain);

                string baseDn = string.Join(",", domain.Split('.').Select(p => "DC=" + p));
                session["LDAP_BASE_DN"] = baseDn;
                session.Log("DetectDomain: LDAP_BASE_DN = " + baseDn);

                session["LDAP_SERVICE_DN"] = "CN=Administrator,CN=Users," + baseDn;
                session.Log("DetectDomain: LDAP_SERVICE_DN = CN=Administrator,CN=Users," + baseDn);

                string dcHost = FindDomainController(domain, session);
                if (!string.IsNullOrEmpty(dcHost))
                {
                    session["LDAP_URL"] = "ldaps://" + dcHost + ":636";
                    session.Log("DetectDomain: LDAP_URL = ldaps://" + dcHost + ":636");
                }

                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log("DetectDomain: unexpected error - " + ex.Message);
                return ActionResult.Success;
            }
        }

        private static string FindDomainController(string domain, Session session)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("nltest", "/dsgetdc:" + domain)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process proc = Process.Start(psi))
                {
                    if (proc == null)
                    {
                        session.Log("DetectDomain: failed to start nltest process.");
                        return null;
                    }

                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);

                    foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int idx = line.IndexOf("DC: \\\\", StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            return line.Substring(idx + 5).Trim();
                        }
                    }

                    session.Log("DetectDomain: nltest output did not contain DC line. Output: " + output);
                }
            }
            catch (Exception ex)
            {
                session.Log("DetectDomain: nltest failed - " + ex.Message);
            }

            return null;
        }
    }
}
