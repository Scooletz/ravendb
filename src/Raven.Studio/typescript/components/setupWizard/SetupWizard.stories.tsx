import { withForceRerender } from "test/storybookTestUtils";
import { withBootstrap5 } from "test/storybookTestUtils";
import { Meta, StoryObj } from "@storybook/react";
import { withStorybookContexts } from "test/storybookTestUtils";
import SetupWizard from "./SetupWizard";
import { mockServices } from "test/mocks/services/MockServices";
import { userEvent, waitFor, expect, waitForElementToBeRemoved } from "@storybook/test";
import { Canvas } from "storybook/internal/types";

enum WizardStep {
    EULA = "EULA",
    SETUP_METHOD = "SETUP_METHOD",
    USE_PACKAGE = "USE_PACKAGE",
    LICENSE_KEY = "LICENSE_KEY",
    SECURITY = "SECURITY",
    DOMAIN = "DOMAIN",
    SELF_SIGNED_CERTIFICATE = "SELF_SIGNED_CERTIFICATE",
    NODE_ADDRESSES = "NODE_ADDRESSES",
    ADDITIONAL_SETTINGS = "ADDITIONAL_SETTINGS",
    SUMMARY = "SUMMARY",
    FINISH = "FINISH",
}

enum SecurityOption {
    DONT_USE_CERTIFICATE = "Don’t use certificate",
    PROVIDE_OWN_CERTIFICATE = "Provide your own certificate",
    GENERATE_LETS_ENCRYPT_CERTIFICATE = "Generate Let’s Encrypt certificate",
}

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
        securityOption: {
            control: {
                type: "select",
            },
            options: [
                SecurityOption.DONT_USE_CERTIFICATE,
                SecurityOption.PROVIDE_OWN_CERTIFICATE,
                SecurityOption.GENERATE_LETS_ENCRYPT_CERTIFICATE,
            ],
        },
    },
    args: {
        securityOption: SecurityOption.GENERATE_LETS_ENCRYPT_CERTIFICATE,
        licenseType: "Community",
    },
} satisfies Meta;

interface SetupWizardStoryArgs {
    licenseType: Raven.Server.Commercial.LicenseType;
    securityOption: SecurityOption;
}

export const Eula: StoryObj = {
    render: () => {
        const { setupWizardService } = mockServices;

        setupWizardService.withEula();
        setupWizardService.withNodesInfoFromPackage();
        setupWizardService.withRegistrationInfo();
        setupWizardService.withHostsForCertificate();
        setupWizardService.withGetSetupLocalNodeIps();
        setupWizardService.withGetSetupParameters();
        setupWizardService.withGetIpsInfo();
        setupWizardService.withCheckDomainAvailability();
        setupWizardService.withClaimDomain();
        setupWizardService.withLetsEncryptAgreement();

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
        await navigateToStep(canvas, WizardStep.SETUP_METHOD);
    },
};

export const UsePackage: StoryObj = {
    ...Eula,
    play: async ({ canvas }) => {
        await navigateToStep(canvas, WizardStep.USE_PACKAGE);
    },
};

export const LicenseKey: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    name: "License key",
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, WizardStep.LICENSE_KEY, args);
    },
};

export const Security: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, WizardStep.SECURITY, args);
    },
};

export const Domain: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, WizardStep.DOMAIN, args);
    },
};

export const SelfSignedCertificate: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    name: "Self-signed certificate",
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, WizardStep.SELF_SIGNED_CERTIFICATE, args);
    },
};

export const NodeAddresses: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    name: "Node addresses",
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, WizardStep.NODE_ADDRESSES, args);
    },
};

export const AdditionalSettings: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    name: "Additional settings",
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, WizardStep.ADDITIONAL_SETTINGS, args);
    },
};

export const Summary: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, WizardStep.SUMMARY, args);
    },
};

export const Finish: StoryObj<SetupWizardStoryArgs> = {
    ...Eula,
    play: async ({ canvas, args }) => {
        await navigateToStep(canvas, WizardStep.FINISH, args);
    },
};


async function navigateToStep(canvas: Canvas, targetStep: WizardStep, args?: SetupWizardStoryArgs): Promise<void> {
    await waitForElementToBeRemoved(canvas.getByTestId("loader"));

    // If target is EULA, we're already there
    if (targetStep === WizardStep.EULA) {
        return;
    }

    // Accept EULA
    const eula = await canvas.findByTestId("eula-bottom");
    await waitFor(() => expect(eula).toBeInTheDocument());
    eula.scrollIntoView({ behavior: "instant" });

    let continueButton = canvas.getByRole("button", { name: /Continue/ });
    await waitFor(() => expect(continueButton).not.toBeDisabled());
    await userEvent.click(continueButton);

    // If target is SETUP_METHOD, we're done
    if (targetStep === WizardStep.SETUP_METHOD) {
        return;
    }

    // Handle USE_PACKAGE path
    if (targetStep === WizardStep.USE_PACKAGE) {
        await userEvent.click(canvas.getByRole("heading", { name: /Use setup package/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        return;
    }

    // For all other steps, go through Set up new cluster path
    await userEvent.click(canvas.getByRole("heading", { name: /Set up new cluster/ }));
    await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));

    // Enter license key
    if (args.licenseType) {
        await userEvent.type(
            canvas.getByTestId("license-key-input"),
            `{{ "Id": "${args.licenseType}", "Name": "RavenDB", "Keys": [[] }`
        );
    }

    if (targetStep === WizardStep.LICENSE_KEY) {
        return;
    }

    // Continue to SECURITY
    continueButton = canvas.getByRole("button", { name: /Continue/ });
    await waitFor(() => expect(continueButton).not.toBeDisabled());
    await userEvent.click(continueButton);

    if (targetStep === WizardStep.SECURITY) {
        return;
    }

    // Handle certificate paths
    // If security option is DONT_USE_CERTIFICATE, we cannot process further until we add a certificate
    // TODO: mock certificate file upload
    if (targetStep === WizardStep.SELF_SIGNED_CERTIFICATE || args.securityOption === SecurityOption.PROVIDE_OWN_CERTIFICATE) {
        await userEvent.click(canvas.getByRole("heading", { name: /Provide your own certificate/ }));
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
        return;
    }

    // For remaining steps, go through Let's Encrypt certificate path
    await userEvent.click(canvas.getByRole("heading", { name: args.securityOption }));
    if (args.securityOption === SecurityOption.GENERATE_LETS_ENCRYPT_CERTIFICATE) {
        await userEvent.click(canvas.getByRole("checkbox"));
    }
    await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));

    if (targetStep === WizardStep.DOMAIN) {
        return;
    }

    // Continue to NODE_ADDRESSES
    if (args.securityOption !== SecurityOption.DONT_USE_CERTIFICATE) {
        await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));
    }

    if (targetStep === WizardStep.NODE_ADDRESSES) {
        return;
    }

    // Save and continue to ADDITIONAL_SETTINGS
    await userEvent.click(canvas.getByRole("button", { name: /Save/ }));
    await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));

    if (targetStep === WizardStep.ADDITIONAL_SETTINGS) {
        return;
    }

    // Continue to SUMMARY
    await userEvent.click(canvas.getByRole("button", { name: /Continue/ }));

    if (targetStep === WizardStep.SUMMARY) {
        return;
    }

    // Finish the wizard
    await userEvent.click(canvas.getByRole("button", { name: /Finish/ }));
}
