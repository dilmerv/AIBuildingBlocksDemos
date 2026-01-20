/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.PackageManager;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Oculus.Interaction.Editor
{
    /// <summary>
    /// If a scene contains required package tags, display an editor window
    /// which prompts the user to add the missing dependencies.
    /// </summary>
    [InitializeOnLoad]
    public static class VerifyScenePackageDependencies
    {
        private enum WindowType
        {
            None,
            RequiredAndRecommended,
            RequiredOnly,
            RecommendedOnly,
        }

        private const string REQUIRED_PACKAGE_LABEL = "isdkRequirePackage";
        private const string RECOMMENDED_PACKAGE_LABEL = "isdkRecommendPackage";
        private const string OPTOUT_RECOMMENDED_KEY_FORMAT = "isdkRecommendPackageOptOutKey_{0}";

        /// <summary>
        /// This scene preprocessor will cause build failures if scenes require packages
        /// that are not present in the project.
        /// </summary>
        public class PreprocessBuild : IProcessSceneWithReport
        {
            public int callbackOrder => 0;

            public void OnProcessScene(Scene scene, BuildReport report)
            {
                var missingPackages = GetRequiredPackages(scene).Except(GetProjectPackages());
                if (missingPackages.Any())
                {
                    StringBuilder sb = new StringBuilder();
                    missingPackages.ToList().ForEach(p => sb.AppendLine(p));
                    throw new BuildFailedException(
                        $"Missing required packages for scene {scene.name}:\n" +
                        sb.ToString());
                }
            }
        }

        static VerifyScenePackageDependencies()
        {
            EditorSceneManager.sceneOpened += HandleSceneOpened;
        }

        private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (Application.isBatchMode ||
                BuildPipeline.isBuildingPlayer)
            {
                return;
            }
            ShowDialog(scene);
        }

        private static void ShowDialog(Scene scene)
        {
            var currentPackages = GetProjectPackages();
            var requiredPackages = GetRequiredPackages(scene).Except(currentPackages);
            var recommendedPackages = GetRecommendedPackages(scene).Except(currentPackages);

            bool anyRequired = requiredPackages.Any();
            bool anyRecommended = recommendedPackages.Any();

            WindowType windowType =
                anyRequired && anyRecommended ?
                    WindowType.RequiredAndRecommended :
                anyRequired && !anyRecommended ?
                    WindowType.RequiredOnly :
                !anyRequired && anyRecommended ?
                    WindowType.RecommendedOnly :
                    WindowType.None;

            if (windowType == WindowType.None)
            {
                return;
            }

            if (windowType == WindowType.RecommendedOnly &&
                GetIgnoreRecommended(scene))
            {
                return;
            }

            StringBuilder windowText = new StringBuilder();

            const string windowTitle = "Missing packages detected";
            const string requiredText = "The following packages are " +
                "REQUIRED for this scene to function:\n";
            const string recommendedText = "The following packages are " +
                "RECOMMENDED to enable full functionality in this scene:\n";

            string primaryButton = string.Empty;
            System.Action primaryAction = delegate { };
            string cancelButton = string.Empty;
            System.Action cancelAction = delegate { };
            string altButton = string.Empty;
            System.Action altAction = delegate { };

            switch (windowType)
            {
                case WindowType.RequiredAndRecommended:
                    SetIgnoreRecommended(scene, false);
                    windowText.AppendLine(requiredText);
                    requiredPackages.ToList().ForEach(p => windowText.AppendLine(p));
                    windowText.AppendLine();
                    windowText.AppendLine(recommendedText);
                    recommendedPackages.ToList().ForEach(p => windowText.AppendLine(p));
                    primaryButton = "Install All";
                    primaryAction = () => Install(requiredPackages.Union(recommendedPackages));
                    cancelButton = "Ignore";
                    altButton = "Install Required Packages Only";
                    altAction = () => Install(requiredPackages);
                    break;
                case WindowType.RequiredOnly:
                    SetIgnoreRecommended(scene, false);
                    windowText.AppendLine(requiredText);
                    requiredPackages.ToList().ForEach(p => windowText.AppendLine(p));
                    primaryButton = "Install";
                    primaryAction = () => Install(requiredPackages);
                    cancelButton = "Ignore";
                    break;
                case WindowType.RecommendedOnly:
                    windowText.AppendLine(recommendedText);
                    recommendedPackages.ToList().ForEach(p => windowText.AppendLine(p));
                    primaryButton = "Install";
                    primaryAction = () => Install(recommendedPackages);
                    cancelButton = "Ignore";
                    altButton = "Ignore - Don't Ask Again";
                    altAction = () => SetIgnoreRecommended(scene, true);
                    break;
            }

            int result = EditorUtility.DisplayDialogComplex(
                    windowTitle, windowText.ToString(),
                    primaryButton, cancelButton, altButton);

            switch (result)
            {
                case 0: primaryAction.Invoke(); break;
                case 1: cancelAction.Invoke(); break;
                case 2: altAction.Invoke(); break;
            }

            void Install(IEnumerable<string> packages)
            {
                EditorApplication.delayCall += () =>
                {
                    InstallVerified(packages);
                };
            }
        }

        private static IEnumerable<string> GetRequiredPackages(Scene scene)
        {
            Object obj = AssetDatabase.LoadMainAssetAtPath(scene.path);
            return AssetDatabase.GetLabels(obj)
                .Where(l => l.StartsWith(REQUIRED_PACKAGE_LABEL))
                .Select(l => l.Remove(0, REQUIRED_PACKAGE_LABEL.Length).TrimStart('-'));
        }

        private static IEnumerable<string> GetRecommendedPackages(Scene scene)
        {
            Object obj = AssetDatabase.LoadMainAssetAtPath(scene.path);
            return AssetDatabase.GetLabels(obj)
                .Where(l => l.StartsWith(RECOMMENDED_PACKAGE_LABEL))
                .Select(l => l.Remove(0, RECOMMENDED_PACKAGE_LABEL.Length).TrimStart('-'));
        }

        private static IEnumerable<string> GetProjectPackages()
        {
            return PackageInfo.GetAllRegisteredPackages().Select(info => info.name);
        }

        private static async void InstallVerified(IEnumerable<string> packages)
        {
            var verifiedPackages = await GetVerifiedVersions(packages);
            if (verifiedPackages.Count() != packages.Count())
            {
                Debug.LogError("Failed to resolve all required packages, aborting installation.");
            }
            else
            {
                Debug.Log("Installing packages...");
                Client.AddAndRemove(verifiedPackages.ToArray());
            }
        }

        private static async Task<IEnumerable<string>> GetVerifiedVersions(IEnumerable<string> packages)
        {
            List<string> verifiedPackages = new List<string>();
            foreach (var package in packages)
            {
                var request = Client.Search(package);
                while (!request.IsCompleted)
                {
                    await Task.Delay(500);
                }

                var recommendedVersion = (request.Status == StatusCode.Success &&
                                          request.Result.Length == 1) ?
#if UNITY_2022_2_OR_NEWER
                    request.Result[0].versions?.recommended : null;
#else
                    request.Result[0].versions?.verified : null;
#endif
                if (request.Status == StatusCode.Success &&
                    request.Result.Length == 1 &&
                    !string.IsNullOrEmpty(recommendedVersion))
                {
                    Debug.Log($"Found recommended version {recommendedVersion} for package {package}");
                    verifiedPackages.Add($"{package}@{recommendedVersion}");
                }
                else
                {
                    Debug.LogError($"Failed to retrieve recommended version of package {package}");
                }
            }
            return verifiedPackages;
        }

        private static bool GetIgnoreRecommended(Scene scene)
        {
            return EditorPrefs.GetBool(string.Format(
                OPTOUT_RECOMMENDED_KEY_FORMAT, scene.name), false);
        }

        private static void SetIgnoreRecommended(Scene scene, bool optOut)
        {
            EditorPrefs.SetBool(string.Format(
                OPTOUT_RECOMMENDED_KEY_FORMAT, scene.name), optOut);
        }
    }
}
