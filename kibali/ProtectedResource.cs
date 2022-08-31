﻿using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Kibali
{
    public class ProtectedResource
    {
        // Permission -> (Methods,Scheme) -> Path  (Darrel's format)
        // (Schemes -> Permissions) -> restriction -> target  (Kanchan's format)
        // target -> restrictions -> schemes -> Ordered Permissions (CSDL Format) 

        // path -> Method -> Schemes -> Permissions  (Inverted format) 

        // (Path, Method) -> Schemes -> Permissions (Docs)
        // (Path, Method) -> Scheme(delegated) -> Permissions (Graph Explorer Tab)
        // Permissions(delegated) (Graph Explorer Permissions List)
        // Schemas -> Permissions ( AAD Onboarding)
        private Dictionary<string, Dictionary<string, HashSet<string>>> leastPrivilegedPermissions { get; set; } = new ();

        public string Url { get; set; }
        public Dictionary<string, Dictionary<string, List<AcceptableClaim>>> SupportedMethods { get; set; } = new Dictionary<string, Dictionary<string, List<AcceptableClaim>>>();

        public bool ContainsErrors = false;

        public HashSet<PermissionsError> PermissionsErrors { get; set; } = new ();

        public ProtectedResource(string url)
        {
            Url = url;
        }

        public void AddRequiredClaims(string permission, PathSet pathSet)
        {
            foreach (var supportedMethod in pathSet.Methods)
            {
                var supportedSchemes = new Dictionary<string, List<AcceptableClaim>>();
                foreach (var supportedScheme in pathSet.SchemeKeys)
                {
                    if (!supportedSchemes.ContainsKey(supportedScheme))
                    {
                        supportedSchemes.Add(supportedScheme, new List<AcceptableClaim>());
                    }
                    supportedSchemes[supportedScheme].Add(new AcceptableClaim(permission, pathSet.AlsoRequires));
                }
                if (!this.SupportedMethods.ContainsKey(supportedMethod))
                {
                    this.SupportedMethods.Add(supportedMethod, supportedSchemes);
                } else
                {
                    Update(this.SupportedMethods[supportedMethod], supportedSchemes);
                };
            }
        }

        public void ValidateLeastPrivilegePermissions(string permission, PathSet pathSet, List<string> leastPrivilegedPermissions, int index)
        {
            ComputeLeastPrivilegeEntries(permission, pathSet, leastPrivilegedPermissions);
            ValidateMismatchedSchemes(permission, pathSet, leastPrivilegedPermissions, index);
            ValidateDuplicatedScopes(permission, index);
        }

        private void ComputeLeastPrivilegeEntries(string permission, PathSet pathSet, List<string> leastPrivilegedPermissions)
        {
            foreach (var supportedMethod in pathSet.Methods)
            {
                var schemeLeastPrivilegeScopes = new Dictionary<string, HashSet<string>>();
                foreach (var supportedScheme in pathSet.SchemeKeys)
                {
                    if (!leastPrivilegedPermissions.Contains(supportedScheme))
                    {
                        continue;
                    }
                    if (!schemeLeastPrivilegeScopes.ContainsKey(supportedScheme))
                    {
                        schemeLeastPrivilegeScopes.Add(supportedScheme, new HashSet<string>());
                    }
                    schemeLeastPrivilegeScopes[supportedScheme].Add(permission);
                }
                if (!this.leastPrivilegedPermissions.ContainsKey(supportedMethod))
                {
                    this.leastPrivilegedPermissions.Add(supportedMethod, schemeLeastPrivilegeScopes);
                }
                else
                {
                    UpdatePrivilegedPermissions(this.leastPrivilegedPermissions[supportedMethod], schemeLeastPrivilegeScopes, supportedMethod);
                };
            }
        }

        private void ValidateDuplicatedScopes(string permission, int index)
        {
            foreach (var methodScopes in this.leastPrivilegedPermissions)
            {
                var method = methodScopes.Key;
                foreach (var schemeScope in methodScopes.Value)
                {
                    var scopes = schemeScope.Value;
                    var scheme = schemeScope.Key;
                    if (scopes.Count > 1)
                    {
                        this.PermissionsErrors.Add(new PermissionsError
                        {
                            Path = this.Url,
                            ErrorCode = PermissionsErrorCode.DuplicateLeastPrivilegeScopes,
                            Message = string.Format(StringConstants.DuplicateLeastPrivilegeSchemeErrorMessage, string.Join(", ", scopes), scheme, method),
                        });
                        this.ContainsErrors = true;
                    }
                }
            }
            
        }

        private void ValidateMismatchedSchemes(string permission, PathSet pathSet, List<string> leastPrivilegedPermissions, int index)
        {
            var mismatchedPrivilegeSchemes = leastPrivilegedPermissions.Except(pathSet.SchemeKeys);
            if (mismatchedPrivilegeSchemes.Count() > 0)
            {
                var invalidSchemes = string.Join(", ", mismatchedPrivilegeSchemes);
                var expectedSchemes = string.Join(", ", pathSet.SchemeKeys);
                this.PermissionsErrors.Add(new PermissionsError
                {
                    Path = this.Url,
                    ErrorCode = PermissionsErrorCode.InvalidLeastPrivilegeScheme,
                    Message = string.Format(StringConstants.UnexpectedLeastPrivilegeSchemeErrorMessage, invalidSchemes, permission, expectedSchemes),
                });
                this.ContainsErrors = true;
            }
        }

        private void UpdatePrivilegedPermissions(Dictionary<string, HashSet<string>> existingPermissions, Dictionary<string, HashSet<string>> newPermissions, string method)
        {
            foreach (var newPermission in newPermissions)
            {
                if (existingPermissions.TryGetValue(newPermission.Key, out var existingPermission))
                {
                    existingPermission.UnionWith(newPermission.Value);
                    
                }
                else
                {
                    existingPermissions[newPermission.Key] = newPermission.Value;
                }
            }
        }

        private void Update(Dictionary<string, List<AcceptableClaim>> existingSchemes, Dictionary<string, List<AcceptableClaim>> newSchemes)
        {
            
            foreach(var newScheme in newSchemes)
            {
                if (existingSchemes.TryGetValue(newScheme.Key, out var existingScheme))
                {
                    existingScheme.AddRange(newScheme.Value);
                } 
                else
                {
                    existingSchemes[newScheme.Key] = newScheme.Value;
                }
            }
        }

        public void Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("url");
            writer.WriteStringValue(Url);
            writer.WritePropertyName("methods");
            WriteSupportedMethod(writer, this.SupportedMethods);
            
            writer.WriteEndObject();
        }

        private void WriteSupportedMethod(Utf8JsonWriter writer, Dictionary<string, Dictionary<string, List<AcceptableClaim>>> supportedMethods)
        {
            writer.WriteStartObject();
            foreach (var item in supportedMethods)
            {
                writer.WritePropertyName(item.Key);
                WriteSupportedSchemes(writer, item.Value);
            }
            writer.WriteEndObject();
        }

        public void WriteSupportedSchemes(Utf8JsonWriter writer, Dictionary<string, List<AcceptableClaim>> methodClaims)
        {
            writer.WriteStartObject();
            foreach (var item in methodClaims)
            {
                writer.WritePropertyName(item.Key);
                WriteAcceptableClaims(writer, item.Value);
            }
            writer.WriteEndObject();
        }

        public void WriteAcceptableClaims(Utf8JsonWriter writer, List<AcceptableClaim> schemes)
        {
            writer.WriteStartArray();
            foreach (var item in schemes)
            {
                item.Write(writer);
            }
            writer.WriteEndArray();
        }
    }

    
}
