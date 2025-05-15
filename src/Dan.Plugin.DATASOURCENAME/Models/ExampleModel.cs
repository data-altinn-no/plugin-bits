using System;
using Newtonsoft.Json;

namespace Dan.Plugin.DATASOURCENAME.Models;

[Serializable]
public class ExampleModel
{
    [JsonRequired]
    public string ResponseField1 { get; set; }

    [JsonRequired]
    public string ResponseField2 { get; set; }
}
