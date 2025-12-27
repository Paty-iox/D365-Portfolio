const typescriptParser = require("@typescript-eslint/parser");
const typescriptPlugin = require("@typescript-eslint/eslint-plugin");

module.exports = [
    {
        ignores: [
            "**/generated/**",
            "**/out/**",
            "**/node_modules/**",
            "**/*.d.ts"
        ]
    },
    {
        files: ["**/*.ts", "**/*.tsx"],
        languageOptions: {
            parser: typescriptParser,
            parserOptions: {
                ecmaVersion: "latest",
                sourceType: "module",
                ecmaFeatures: {
                    jsx: true
                }
            }
        },
        plugins: {
            "@typescript-eslint": typescriptPlugin
        },
        rules: {
            "no-unused-vars": "off",
            "@typescript-eslint/no-unused-vars": "warn",
            "no-undef": "off"
        }
    }
];
