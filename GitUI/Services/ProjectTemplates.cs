using System;
using System.Collections.Generic;

namespace GitUI.Services;

public record TemplateFile(string Path, string Content);

public record ProjectTemplate(
    string Key,
    string DisplayName,
    string? GitignoreSuggestion,
    IReadOnlyList<TemplateFile> Files);

/// <summary>
/// Starter file sets for various languages, applied after a new repository is created.
/// Placeholders: {REPO_NAME}, {OWNER} are replaced at upload time.
/// </summary>
public static class ProjectTemplates
{
    public static readonly ProjectTemplate None = new("none", "(없음)", null, Array.Empty<TemplateFile>());

    public static readonly IReadOnlyList<ProjectTemplate> All = new[]
    {
        None,

        new ProjectTemplate("python", "Python", "Python", new[]
        {
            new TemplateFile("main.py",
                "def main():\n" +
                "    print(\"Hello, world!\")\n\n" +
                "if __name__ == \"__main__\":\n" +
                "    main()\n"),
            new TemplateFile("requirements.txt", "# Add your dependencies here\n"),
            new TemplateFile(".python-version", "3.12\n"),
        }),

        new ProjectTemplate("node", "Node.js (JavaScript)", "Node", new[]
        {
            new TemplateFile("package.json",
                "{\n" +
                "  \"name\": \"{REPO_NAME}\",\n" +
                "  \"version\": \"0.1.0\",\n" +
                "  \"description\": \"\",\n" +
                "  \"main\": \"index.js\",\n" +
                "  \"scripts\": {\n" +
                "    \"start\": \"node index.js\",\n" +
                "    \"test\": \"echo \\\"Error: no test specified\\\" && exit 1\"\n" +
                "  },\n" +
                "  \"author\": \"\",\n" +
                "  \"license\": \"ISC\"\n" +
                "}\n"),
            new TemplateFile("index.js", "console.log(\"Hello, world!\");\n"),
            new TemplateFile(".nvmrc", "20\n"),
        }),

        new ProjectTemplate("typescript", "TypeScript (Node)", "Node", new[]
        {
            new TemplateFile("package.json",
                "{\n" +
                "  \"name\": \"{REPO_NAME}\",\n" +
                "  \"version\": \"0.1.0\",\n" +
                "  \"main\": \"dist/index.js\",\n" +
                "  \"scripts\": {\n" +
                "    \"build\": \"tsc\",\n" +
                "    \"start\": \"node dist/index.js\",\n" +
                "    \"dev\": \"ts-node src/index.ts\"\n" +
                "  },\n" +
                "  \"devDependencies\": {\n" +
                "    \"typescript\": \"^5.4.0\",\n" +
                "    \"ts-node\": \"^10.9.2\",\n" +
                "    \"@types/node\": \"^20.11.0\"\n" +
                "  }\n" +
                "}\n"),
            new TemplateFile("tsconfig.json",
                "{\n" +
                "  \"compilerOptions\": {\n" +
                "    \"target\": \"ES2022\",\n" +
                "    \"module\": \"commonjs\",\n" +
                "    \"strict\": true,\n" +
                "    \"esModuleInterop\": true,\n" +
                "    \"outDir\": \"dist\",\n" +
                "    \"rootDir\": \"src\"\n" +
                "  },\n" +
                "  \"include\": [\"src/**/*\"]\n" +
                "}\n"),
            new TemplateFile("src/index.ts",
                "function main(): void {\n" +
                "  console.log(\"Hello, world!\");\n" +
                "}\n\n" +
                "main();\n"),
        }),

        new ProjectTemplate("dotnet", "C# (.NET 8 Console)", "VisualStudio", new[]
        {
            new TemplateFile("{REPO_NAME}.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n\n" +
                "  <PropertyGroup>\n" +
                "    <OutputType>Exe</OutputType>\n" +
                "    <TargetFramework>net8.0</TargetFramework>\n" +
                "    <ImplicitUsings>enable</ImplicitUsings>\n" +
                "    <Nullable>enable</Nullable>\n" +
                "  </PropertyGroup>\n\n" +
                "</Project>\n"),
            new TemplateFile("Program.cs",
                "Console.WriteLine(\"Hello, world!\");\n"),
            new TemplateFile("global.json",
                "{\n" +
                "  \"sdk\": {\n" +
                "    \"version\": \"8.0.0\",\n" +
                "    \"rollForward\": \"latestMinor\"\n" +
                "  }\n" +
                "}\n"),
        }),

        new ProjectTemplate("go", "Go", "Go", new[]
        {
            new TemplateFile("go.mod",
                "module github.com/{OWNER}/{REPO_NAME}\n\n" +
                "go 1.22\n"),
            new TemplateFile("main.go",
                "package main\n\n" +
                "import \"fmt\"\n\n" +
                "func main() {\n" +
                "    fmt.Println(\"Hello, world!\")\n" +
                "}\n"),
        }),

        new ProjectTemplate("rust", "Rust", "Rust", new[]
        {
            new TemplateFile("Cargo.toml",
                "[package]\n" +
                "name = \"{REPO_NAME}\"\n" +
                "version = \"0.1.0\"\n" +
                "edition = \"2021\"\n\n" +
                "[dependencies]\n"),
            new TemplateFile("src/main.rs",
                "fn main() {\n" +
                "    println!(\"Hello, world!\");\n" +
                "}\n"),
        }),

        new ProjectTemplate("java", "Java (Maven)", "Java", new[]
        {
            new TemplateFile("pom.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<project xmlns=\"http://maven.apache.org/POM/4.0.0\">\n" +
                "    <modelVersion>4.0.0</modelVersion>\n" +
                "    <groupId>com.example</groupId>\n" +
                "    <artifactId>{REPO_NAME}</artifactId>\n" +
                "    <version>0.1.0</version>\n" +
                "    <properties>\n" +
                "        <maven.compiler.source>17</maven.compiler.source>\n" +
                "        <maven.compiler.target>17</maven.compiler.target>\n" +
                "        <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>\n" +
                "    </properties>\n" +
                "</project>\n"),
            new TemplateFile("src/main/java/Main.java",
                "public class Main {\n" +
                "    public static void main(String[] args) {\n" +
                "        System.out.println(\"Hello, world!\");\n" +
                "    }\n" +
                "}\n"),
        }),

        new ProjectTemplate("cpp", "C++ (CMake)", "C++", new[]
        {
            new TemplateFile("CMakeLists.txt",
                "cmake_minimum_required(VERSION 3.20)\n" +
                "project({REPO_NAME} CXX)\n\n" +
                "set(CMAKE_CXX_STANDARD 20)\n" +
                "set(CMAKE_CXX_STANDARD_REQUIRED ON)\n\n" +
                "add_executable({REPO_NAME} src/main.cpp)\n"),
            new TemplateFile("src/main.cpp",
                "#include <iostream>\n\n" +
                "int main() {\n" +
                "    std::cout << \"Hello, world!\\n\";\n" +
                "    return 0;\n" +
                "}\n"),
        }),

        new ProjectTemplate("web", "Web (HTML/CSS/JS)", null, new[]
        {
            new TemplateFile("index.html",
                "<!DOCTYPE html>\n" +
                "<html lang=\"en\">\n" +
                "<head>\n" +
                "    <meta charset=\"UTF-8\">\n" +
                "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n" +
                "    <title>{REPO_NAME}</title>\n" +
                "    <link rel=\"stylesheet\" href=\"style.css\">\n" +
                "</head>\n" +
                "<body>\n" +
                "    <h1>Hello, world!</h1>\n" +
                "    <script src=\"script.js\"></script>\n" +
                "</body>\n" +
                "</html>\n"),
            new TemplateFile("style.css",
                "body {\n" +
                "    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;\n" +
                "    max-width: 720px;\n" +
                "    margin: 2rem auto;\n" +
                "    padding: 0 1rem;\n" +
                "}\n"),
            new TemplateFile("script.js", "console.log(\"Hello, world!\");\n"),
        }),

        new ProjectTemplate("react-vite", "React (Vite + TypeScript)", "Node", new[]
        {
            new TemplateFile("package.json",
                "{\n" +
                "  \"name\": \"{REPO_NAME}\",\n" +
                "  \"private\": true,\n" +
                "  \"version\": \"0.0.0\",\n" +
                "  \"type\": \"module\",\n" +
                "  \"scripts\": {\n" +
                "    \"dev\": \"vite\",\n" +
                "    \"build\": \"tsc -b && vite build\",\n" +
                "    \"preview\": \"vite preview\"\n" +
                "  },\n" +
                "  \"dependencies\": {\n" +
                "    \"react\": \"^18.3.1\",\n" +
                "    \"react-dom\": \"^18.3.1\"\n" +
                "  },\n" +
                "  \"devDependencies\": {\n" +
                "    \"@types/react\": \"^18.3.3\",\n" +
                "    \"@types/react-dom\": \"^18.3.0\",\n" +
                "    \"@vitejs/plugin-react\": \"^4.3.1\",\n" +
                "    \"typescript\": \"^5.5.3\",\n" +
                "    \"vite\": \"^5.4.1\"\n" +
                "  }\n" +
                "}\n"),
            new TemplateFile("index.html",
                "<!doctype html>\n" +
                "<html lang=\"en\">\n" +
                "  <head>\n" +
                "    <meta charset=\"UTF-8\" />\n" +
                "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />\n" +
                "    <title>{REPO_NAME}</title>\n" +
                "  </head>\n" +
                "  <body>\n" +
                "    <div id=\"root\"></div>\n" +
                "    <script type=\"module\" src=\"/src/main.tsx\"></script>\n" +
                "  </body>\n" +
                "</html>\n"),
            new TemplateFile("vite.config.ts",
                "import { defineConfig } from 'vite';\n" +
                "import react from '@vitejs/plugin-react';\n\n" +
                "export default defineConfig({\n" +
                "  plugins: [react()],\n" +
                "});\n"),
            new TemplateFile("tsconfig.json",
                "{\n" +
                "  \"compilerOptions\": {\n" +
                "    \"target\": \"ES2020\",\n" +
                "    \"useDefineForClassFields\": true,\n" +
                "    \"lib\": [\"ES2020\", \"DOM\", \"DOM.Iterable\"],\n" +
                "    \"module\": \"ESNext\",\n" +
                "    \"jsx\": \"react-jsx\",\n" +
                "    \"strict\": true,\n" +
                "    \"moduleResolution\": \"bundler\",\n" +
                "    \"allowImportingTsExtensions\": true,\n" +
                "    \"noEmit\": true\n" +
                "  },\n" +
                "  \"include\": [\"src\"]\n" +
                "}\n"),
            new TemplateFile("src/main.tsx",
                "import React from 'react';\n" +
                "import ReactDOM from 'react-dom/client';\n" +
                "import App from './App';\n\n" +
                "ReactDOM.createRoot(document.getElementById('root')!).render(\n" +
                "  <React.StrictMode>\n" +
                "    <App />\n" +
                "  </React.StrictMode>\n" +
                ");\n"),
            new TemplateFile("src/App.tsx",
                "function App() {\n" +
                "  return <h1>Hello, {REPO_NAME}!</h1>;\n" +
                "}\n\n" +
                "export default App;\n"),
        }),

        new ProjectTemplate("kotlin", "Kotlin (Gradle)", "Java", new[]
        {
            new TemplateFile("build.gradle.kts",
                "plugins {\n" +
                "    kotlin(\"jvm\") version \"2.0.0\"\n" +
                "    application\n" +
                "}\n\n" +
                "group = \"com.example\"\n" +
                "version = \"0.1.0\"\n\n" +
                "repositories { mavenCentral() }\n\n" +
                "application {\n" +
                "    mainClass.set(\"MainKt\")\n" +
                "}\n"),
            new TemplateFile("settings.gradle.kts",
                "rootProject.name = \"{REPO_NAME}\"\n"),
            new TemplateFile("src/main/kotlin/Main.kt",
                "fun main() {\n" +
                "    println(\"Hello, world!\")\n" +
                "}\n"),
        }),

        new ProjectTemplate("swift", "Swift (Package)", "Swift", new[]
        {
            new TemplateFile("Package.swift",
                "// swift-tools-version: 5.10\n" +
                "import PackageDescription\n\n" +
                "let package = Package(\n" +
                "    name: \"{REPO_NAME}\",\n" +
                "    targets: [\n" +
                "        .executableTarget(name: \"{REPO_NAME}\", path: \"Sources\")\n" +
                "    ]\n" +
                ")\n"),
            new TemplateFile("Sources/main.swift",
                "print(\"Hello, world!\")\n"),
        }),
    };

    public static string ApplyPlaceholders(string content, string repoName, string owner)
        => content.Replace("{REPO_NAME}", repoName).Replace("{OWNER}", owner);
}
