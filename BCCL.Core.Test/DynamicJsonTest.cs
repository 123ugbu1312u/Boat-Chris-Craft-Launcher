using System;
using System.Dynamic;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BCCL.Core.Utils;

namespace BCCL.Core.Test
{
    [TestClass]
    public class DynamicJsonTest
    {
        [TestMethod]
        public void TestDynamicJson()
        {
            dynamic obj = new { a = 123, b = new { c = this, d = "text" } };
            var options = new JsonSerializerOptions(JsonCompat.SerializerOptions)
            {
                WriteIndented = true
            };
            string json = JsonSerializer.Serialize(obj, options);
            Console.WriteLine(json);
            dynamic? obj2 = JsonSerializer.Deserialize<ExpandoObject>(json, options);
            Console.WriteLine(obj2?.ToString());
        }
    }
}
