using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using HidWizards.UCR.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace HidWizards.UCR.Core.Persistence
{
    internal sealed class UcrJsonSerializer
    {
        private readonly JsonSerializerSettings _settings;

        public UcrJsonSerializer(IEnumerable<Type> pluginTypes)
        {
            var allowedPluginTypes = (pluginTypes ?? Enumerable.Empty<Type>())
                .Where(type => type != null && typeof(Plugin).IsAssignableFrom(type) && !type.IsAbstract)
                .Distinct()
                .ToList();

            var resolver = new UcrJsonContractResolver();
            var plainSettings = CreateBaseSettings(resolver);
            _settings = CreateBaseSettings(resolver);
            _settings.Converters.Add(new PluginJsonConverter(allowedPluginTypes, plainSettings));
        }

        public string Serialize<T>(T value)
        {
            return JsonConvert.SerializeObject(value, _settings);
        }

        public T Deserialize<T>(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return JsonConvert.DeserializeObject<T>(json, _settings);
        }

        private static JsonSerializerSettings CreateBaseSettings(IContractResolver resolver)
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = resolver,
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Error,
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Error
            };
            settings.Converters.Add(new StringEnumConverter());
            return settings;
        }
    }

    internal sealed class UcrJsonContractResolver : CamelCasePropertyNamesContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            if (member.GetCustomAttributes(typeof(XmlIgnoreAttribute), true).Any())
            {
                property.Ignored = true;
            }
            return property;
        }
    }

    internal sealed class PluginJsonConverter : JsonConverter
    {
        private readonly Dictionary<string, Type> _typesById;
        private readonly Dictionary<Type, string> _idsByType;
        private readonly JsonSerializerSettings _plainSettings;

        public PluginJsonConverter(IEnumerable<Type> allowedPluginTypes, JsonSerializerSettings plainSettings)
        {
            if (plainSettings == null) throw new ArgumentNullException(nameof(plainSettings));
            _plainSettings = plainSettings;
            _typesById = new Dictionary<string, Type>(StringComparer.Ordinal);
            _idsByType = new Dictionary<Type, string>();

            foreach (var type in allowedPluginTypes ?? Enumerable.Empty<Type>())
            {
                var id = type.FullName;
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (_typesById.ContainsKey(id) && _typesById[id] != type)
                {
                    throw new InvalidOperationException("Duplicate UCR plugin type identifier: " + id);
                }
                _typesById[id] = type;
                _idsByType[type] = id;
            }
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType != null && typeof(Plugin).IsAssignableFrom(objectType);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var type = value.GetType();
            string typeId;
            if (!_idsByType.TryGetValue(type, out typeId))
            {
                throw new JsonSerializationException("Plugin type is not allowed in UCR persistence: " + type.FullName);
            }

            var plainSerializer = JsonSerializer.Create(_plainSettings);
            var wrapper = new JObject
            {
                ["pluginType"] = typeId,
                ["data"] = JObject.FromObject(value, plainSerializer)
            };
            wrapper.WriteTo(writer);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var wrapper = JObject.Load(reader);
            var typeId = (string)wrapper["pluginType"];
            if (string.IsNullOrWhiteSpace(typeId))
            {
                throw new JsonSerializationException("Persisted plugin is missing pluginType.");
            }

            Type pluginType;
            if (!_typesById.TryGetValue(typeId, out pluginType))
            {
                throw new JsonSerializationException("Persisted plugin type is not installed or allowed: " + typeId);
            }

            var data = wrapper["data"] as JObject;
            if (data == null)
            {
                throw new JsonSerializationException("Persisted plugin data is missing for: " + typeId);
            }

            var plainSerializer = JsonSerializer.Create(_plainSettings);
            using (var dataReader = data.CreateReader())
            {
                return plainSerializer.Deserialize(dataReader, pluginType);
            }
        }

        public override bool CanWrite => true;
        public override bool CanRead => true;
    }
}
