using System.Globalization;
using System.Text.Json;

public interface IRegisterInfoProvider
{
    public IEnumerable<RegisterInfo> GetParameters();
}

public sealed class JSONRegisterInfoProvider : IRegisterInfoProvider
{
    public JsonDocument jsonDocument;
    string[] zavodSetup = { "c_2688208", "c_2688214", "c_2688220", "c_2688226", "c_2688232", "c_2688238" };

    public JSONRegisterInfoProvider(string modelId)
    {
        string path = $"{modelId}.json";
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File {path} not found");
        }
        jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
    }

    public IEnumerable<RegisterInfo> GetParameters()
    {
        var root = jsonDocument.RootElement;
        var array = root.GetProperty("parameters").EnumerateArray();
        foreach (var item in array)
        {
            var parameter = new RegisterInfo();
            var category = item.GetProperty("category").GetString() ?? string.Empty;
            if (zavodSetup.Contains(category))
            {
                continue;
            }

            parameter.Address = item.GetProperty("address").GetString() ?? string.Empty;
            parameter.Multiplier = double.TryParse(item.GetProperty("multiplier").GetString(), CultureInfo.GetCultureInfo("en-US"), out double multiplier) ? multiplier : 1.0;
            var name_variants = item.GetProperty("name").EnumerateObject();
            parameter.Name = string.Empty;
            foreach (var name_variant in name_variants)
            {
                if (name_variant.Name == "ru-RU")
                {
                    parameter.Name = name_variant.Value.GetString() ?? string.Empty;
                }
            }

            yield return parameter;
        }
    }

    public IEnumerable<RegisterInfo> GetOperationalParameters()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<RegisterInfo> GetStaticParameters()
    {
        throw new NotImplementedException();
    }

}
