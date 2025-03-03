import { withForceRerender } from "test/storybookTestUtils";
import { withBootstrap5 } from "test/storybookTestUtils";
import { Meta } from "@storybook/react/*";
import { withStorybookContexts } from "test/storybookTestUtils";
import SetupWizard from "./SetupWizard";
import { mockServices } from "test/mocks/services/MockServices";

export default {
    title: "Setup Wizard",
    decorators: [withStorybookContexts, withBootstrap5, withForceRerender],
} satisfies Meta;

export const Default = () => {
    const { setupWizardService } = mockServices;

    setupWizardService.withEula();

    return (
        <div style={{ height: 800 }}>
            <SetupWizard />
        </div>
    );
};
