using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ProxyService
{
    public static class Utils
    {
        public static string RemoveAbsoluteName(string s)
        {
            string[] spaceSplits = s.Split(' ');

            int slashInd = spaceSplits[1].IndexOf('/', 7);

            if (slashInd == -1)
                return s;

            spaceSplits[1] = spaceSplits[1].Substring(slashInd);

            return string.Join(" ", spaceSplits);
        }

        public static IPAddress GetIP(string hostname)
        {
            return Dns.GetHostEntry(hostname).AddressList[0];
        }
    }
}
