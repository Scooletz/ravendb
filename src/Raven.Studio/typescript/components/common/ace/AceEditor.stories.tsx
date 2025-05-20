import { Meta, StoryObj } from "@storybook/react";
import { withStorybookContexts, withBootstrap5 } from "test/storybookTestUtils";
import AceEditor from "./AceEditor";
import AceUnifiedDiff from "./AceUnifiedDiff";

export default {
    title: "Bits/AceEditor",
    component: AceEditor,
    decorators: [withStorybookContexts, withBootstrap5],
    parameters: {
        design: {
            type: "figma",
            url: "https://www.figma.com/design/ITHbe2U19Ok7cjbEzYa4cb/Design-System-RavenDB-Studio?node-id=3-16049",
        },
    },
} satisfies Meta;

export const JavascriptEditor: StoryObj = {
    name: "Ace Editor",
    render: () => <AceEditor mode="javascript" />,
};

export const UnifiedDiff: StoryObj = {
    render: () => {
        const oldDoc = `{
    "Name": "Original Frankfurter grüne Soße",
    "Supplier": "suppliers/12-A",
    "Category": "categories/2-A",
    "QuantityPerUnit": "12 boxes",
    "PricePerUnit": 13,
    "UnitsInStock": 12,
    "UnitsOnOrder": 32,
    "Discontinued": false,
    "ReorderLevel": 15,
}`;

        const newDoc = `{
    "Name": "Original Frankfurter grüne Soße",
    "Supplier": "suppliers/12-A",
    "Category": "categories/2-A",
    "QuantityPerUnit": "12 boxes",
    "PricePerUnit": 16,
    "UnitsInStock": 12,
    "Discontinued": false,
    "ReorderLevel": 15,
    "@metadata": {
        "@collection": "Products",
    }
}`;

        return <AceUnifiedDiff mode="json" value1={oldDoc} value2={newDoc} height="400px" />;
    },
};
