import { withForceRerender } from "test/storybookTestUtils";
import { withBootstrap5 } from "test/storybookTestUtils";
import { Meta, StoryObj } from "@storybook/react";
import { withStorybookContexts } from "test/storybookTestUtils";
import SetupWizard from "./SetupWizard";
import { mockServices } from "test/mocks/services/MockServices";
import { userEvent, waitFor, expect } from "@storybook/test";
import { Canvas } from "storybook/internal/types";

export default {
    title: "Setup Wizard",
    decorators: [withStorybookContexts, withBootstrap5, withForceRerender],
    argTypes: {
        licenseType: {
            control: {
                type: "select",
            },
            options: [
                "Community",
                "Developer",
                "Enterprise",
                "Essential",
                "Invalid",
                "None",
                "Professional",
                "Reserved",
            ] satisfies Raven.Server.Commercial.LicenseType[],
        },
    },
    args: {
        licenseType: "Community",
    },
} satisfies Meta;

interface SetupWizardStoryArgs {
    licenseType: Raven.Server.Commercial.LicenseType;
}

export const Eula: StoryObj = {
    render: () => {
        const { setupWizardService } = mockServices;

        setupWizardService.withEula();
        setupWizardService.withNodesInfoFromPackage();
        setupWizardService.withRegistrationInfo();
        setupWizardService.withHostsForCertificate();

        return (
            <div style={{ height: 1000 }}>
                <SetupWizard />
            </div>
        );
    },
};

export const SetupMethod: StoryObj = {
    ...Eula,
    play: async ({ canvas }) => {
        await goToSetupStep(canvas);
    },
};

export const UsePackage: StoryObj = {
    ...Eula,
    play: async ({ canvas }) => {
        await goToSetupStep(canvas);
        await userEvent.click(canvas.getByRole("heading", { name: /Use setup package/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    },
};

export const LicenseKeyCommunity: StoryObj = {
    ...Eula,
    name: "License key (Community)",
    play: async ({ canvas }) => {
        await goToLicenseKeyStep(canvas, "Community");
    },
};

export const LicenseKeyDeveloper: StoryObj = {
    ...Eula,
    name: "License key (Developer)",
    play: async ({ canvas }) => {
        await goToLicenseKeyStep(canvas, "Developer");
    },
};

export const LicenseKeyEnterprise: StoryObj = {
    ...Eula,
    name: "License key (Enterprise)",
    play: async ({ canvas }) => {
        await goToLicenseKeyStep(canvas, "Enterprise");
    },
};

export const Security: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    play: async ({ canvas, args }) => {
        await goToSecurityStep(canvas, args.licenseType);
    },
};

export const Domain: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    play: async ({ canvas, args }) => {
        await goToSecurityStep(canvas, args.licenseType);
        await userEvent.click(canvas.getByRole("heading", { name: /Generate Let’s Encrypt certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    },
};

export const SelfSignedCertificate: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    name: "Self-signed certificate",
    play: async ({ canvas, args }) => {
        await goToSecurityStep(canvas, args.licenseType);
        await userEvent.click(canvas.getByRole("heading", { name: /Provide your own certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    },
};

export const NodeAddresses: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    name: "Node addresses",
    play: async ({ canvas, args }) => {
        await goToSecurityStep(canvas, args.licenseType);
        await userEvent.click(canvas.getByRole("heading", { name: /Generate Let’s Encrypt certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    },
};

export const AdditionalSettings: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    name: "Additional settings",
    play: async ({ canvas, args }) => {
        await goToSecurityStep(canvas, args.licenseType);
        await userEvent.click(canvas.getByRole("heading", { name: /Generate Let’s Encrypt certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    },
};

export const Summary: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    play: async ({ canvas, args }) => {
        await goToSecurityStep(canvas, args.licenseType);
        await userEvent.click(canvas.getByRole("heading", { name: /Generate Let’s Encrypt certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    },
};

export const Finish: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    play: async ({ canvas, args }) => {
        await goToSecurityStep(canvas, args.licenseType);
        await userEvent.click(canvas.getByRole("heading", { name: /Generate Let’s Encrypt certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Finish/ }));
    },
};

async function goToSetupStep(canvas: Canvas) {
    const eula = await canvas.findByTestId("eula-bottom");
    await waitFor(() => expect(eula).toBeInTheDocument());
    eula.scrollIntoView({ behavior: "instant" });

    const continueButton = canvas.getByRole("button", { name: /Continue/ });
    await waitFor(() => expect(continueButton).not.toBeDisabled());

    await userEvent.click(continueButton);
}

async function goToLicenseKeyStep(canvas: Canvas, licenseType: Raven.Server.Commercial.LicenseType) {
    await goToSetupStep(canvas);
    await userEvent.click(canvas.getByRole("heading", { name: /Set up new cluster/ }));
    await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));

    // userEvent.type don't work with single '{' and '['
    await userEvent.type(
        canvas.getByTestId("license-key-input"),
        `{{ "Id": "${licenseType}", "Name": "RavenDB", "Keys": [[] }`
    );
}

async function goToSecurityStep(canvas: Canvas, licenseType: Raven.Server.Commercial.LicenseType) {
    await goToLicenseKeyStep(canvas, licenseType);

    const continueButton = canvas.getByRole("button", { name: /Continue/ });
    await waitFor(() => expect(continueButton).not.toBeDisabled());
    await userEvent.click(continueButton);
}
