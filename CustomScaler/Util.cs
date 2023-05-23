using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomScaler
{
    public static class Util
    {
        public static string[] SupportedConditionsWithinQuery = { ">=", "<=", "==", ">", "<" };

        public static string[] SupportedConditions = { "and", "or" };
        public static Dictionary<string, string> GetQueryData(string query)
        {
            var queryDict = new Dictionary<string, string>();
            foreach (var condition in SupportedConditions)
            {
                if (query.Contains(condition))
                {
                    var splitQuery = query.Split(condition);
                    foreach (var item in splitQuery)
                    {
                        var result = SplitQuery(item);
                        if (result != null)
                            queryDict.Add(result.Item1, result.Item2);
                    }

                }
            }
            return queryDict;
        }

        public static Tuple<string, string>? SplitQuery(string query)
        {
            foreach (var condition in SupportedConditionsWithinQuery)
            {
                if (query.Contains(condition))
                {
                    var splitValue = query.Split(condition);
                    return Tuple.Create(splitValue[0], splitValue[1]);
                }

            }
            return null;
        }
    }
}
