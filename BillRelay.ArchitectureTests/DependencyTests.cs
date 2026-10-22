using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.NUnit;

using NUnit.Framework;

using BillRelay.Configuration;
using BillRelay.Services;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using ReflectionAssembly = System.Reflection.Assembly;

namespace BillRelay.ArchitectureTests
{
    [TestFixture]
    public sealed class DependencyTests
    {
        private static readonly ReflectionAssembly productionAssembly = typeof(BillRelaySettings).Assembly;
        private static readonly Architecture architecture = new ArchLoader()
            .LoadAssemblies(productionAssembly)
            .Build();

        [Test]
        public void GivenConfigurationTypes_WhenCheckingDependencies_ThenServicesAreNotReferenced()
        {
            Assert.That(productionAssembly.GetTypes().Any(type =>
                string.Equals(type.Namespace, "BillRelay.Configuration", StringComparison.Ordinal)));

            Types().That().ResideInNamespace("BillRelay.Configuration")
                .Should().NotDependOnAny(Types().That().ResideInNamespace("BillRelay.Services"))
                .Because("Configuration must remain independent of service implementation and persistence.")
                .Check(architecture);
        }

        [Test]
        public void GivenServices_WhenCheckingDependencies_ThenTheCompositionRootIsNotReferenced()
        {
            Assert.That(productionAssembly.GetTypes().Any(type =>
                string.Equals(type.Namespace, "BillRelay.Services", StringComparison.Ordinal)));

            Types().That().ResideInNamespace("BillRelay.Services")
                .Should().NotDependOnAny(Types().That().HaveFullName(typeof(Program).FullName!))
                .Because("Services must not depend on the application composition root.")
                .Check(architecture);
        }

        [TestCase("Microsoft.Data.Sqlite")]
        [TestCase("System.Data.SqlClient")]
        [TestCase("Microsoft.EntityFrameworkCore")]
        public void GivenProductionTypes_WhenCheckingPersistence_ThenDatabaseDependenciesAreForbidden(
            string forbiddenNamespace)
        {
            Assert.That(architecture.Types, Is.Not.Empty);

            Types().Should().NotDependOnAny(Types().That().ResideInNamespace(forbiddenNamespace))
                .Because("BillRelay persists plaintext files through NuciDAL, not a database.")
                .Check(architecture);
        }

        [Test]
        public void GivenProductionAssembly_WhenInspectingVisibility_ThenNoTestFriendAssembliesExist() =>
            Assert.That(productionAssembly.GetCustomAttributes<InternalsVisibleToAttribute>(), Is.Empty);

        [Test]
        public void GivenProductionProject_WhenInspectingReferences_ThenOnlyProductionDependenciesExist()
        {
            XDocument project = XDocument.Load(FindProductionProject());
            string[] packageNames = project.Descendants("PackageReference")
                .Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)
                .ToArray();

            Assert.That(packageNames, Does.Contain("NuciDAL"));
            Assert.That(packageNames.Where(IsForbiddenDependency), Is.Empty,
                "Production must not reference database providers or test frameworks.");
            Assert.That(project.Descendants("ProjectReference"), Is.Empty,
                "The current single production project must not reference test projects.");
            Assert.That(project.Descendants("InternalsVisibleTo"), Is.Empty);
            Assert.That(project.Descendants("AssemblyAttribute").Where(attribute =>
                ((string?)attribute.Attribute("Include") ?? string.Empty)
                    .Contains("InternalsVisibleTo", StringComparison.Ordinal)), Is.Empty);
            Assert.That(productionAssembly.GetReferencedAssemblies().Where(reference =>
                IsForbiddenDependency(reference.Name ?? string.Empty)), Is.Empty);
        }

        private static bool IsForbiddenDependency(string name) =>
            name.Contains("Sql", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Dapper", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Dynamo", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Mongo", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("NUnit", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Moq", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Tests", StringComparison.OrdinalIgnoreCase);

        private static string FindProductionProject()
        {
            DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);

            while (directory is not null)
            {
                string projectPath = Path.Combine(directory.FullName, "BillRelay", "BillRelay.csproj");

                if (File.Exists(projectPath))
                {
                    return projectPath;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("The BillRelay production project could not be located.");
        }
    }
}