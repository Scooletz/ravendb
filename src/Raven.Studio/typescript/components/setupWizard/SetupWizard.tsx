import "./SetupWizard.scss";
import Button from "react-bootstrap/Button";
import { Icon } from "components/common/Icon";
import { FormProvider, useForm, useWatch } from "react-hook-form";
import {
    SetupWizardFormData,
    setupWizardSchema,
    SetupWizardSetupMethod,
    SetupWizardStepId,
    SetupWizardSecurityOption,
} from "./setupWizardValidation";
import { yupResolver } from "@hookform/resolvers/yup";
import { NumberedList } from "components/common/NumberedList";
import SetupWizardEulaStep from "./steps/SetupWizardEulaStep";
import SetupWizardSetupMethodStep from "./steps/SetupWizardSetupMethodStep";
import SetupWizardLicenseKeyStep from "./steps/SetupWizardLicenseKeyStep";
import { useMemo } from "react";
import classNames from "classnames";
import { SetupWizardStepItem } from "./partials/SetupWizardStepItem";
import SetupWizardSecurityStep from "./steps/SetupWizardSecurityStep";
import SetupWizardSelfSignedCertificateStep from "./steps/SetupWizardSelfSignedCertificateStep";
import SetupWizardDomainStep from "./steps/SetupWizardDomainStep";
import SetupWizardNodeAddressStep from "./steps/SetupWizardNodeAddressStep";
import SetupWizardAdditionalSettingsStep from "./steps/SetupWizardAdditionalSettingsStep";
import SetupWizardFinishStep from "./steps/SetupWizardFinishStep";
import SetupWizardSummaryStep from "./steps/SetupWizardSummaryStep";
import SetupWizardUsePackageStep from "./steps/SetupWizardUsePackageStep";

const ravenLogo = require("Content/img/ravendb_logo.svg");

export default function SetupWizard() {
    const form = useForm<SetupWizardFormData>({
        resolver: yupResolver(setupWizardSchema),
        defaultValues: {
            currentStep: "Eula",
            setupMethodStep: {
                method: null,
            },
            securityStep: {
                securityOption: null,
            },
        },
    });

    const { setValue, handleSubmit } = form;

    const formValues = useWatch({ control: form.control });

    const steps = useMemo(
        () =>
            getAvailableSteps(
                formValues.currentStep,
                formValues.setupMethodStep.method,
                formValues.securityStep.securityOption
            ),
        [formValues.currentStep, formValues.setupMethodStep.method, formValues.securityStep.securityOption]
    );

    const currentStepIdx = steps.findIndex((x) => x.isCurrent);

    const handleContinue = () => {
        setValue("currentStep", steps[currentStepIdx + 1].title);
    };

    return (
        <FormProvider {...form}>
            <form onSubmit={handleSubmit(console.log)} className="h-100">
                <div className="setup-wizard-container">
                    <div className="setup-wizard-main">
                        <div className="d-flex flex-column h-100 w-75">
                            <div className="mt-4">
                                <img src={ravenLogo} alt="RavenDB Logo" width="120" />
                            </div>
                            <div className="overflow-y-auto py-4">{steps[currentStepIdx].component}</div>
                        </div>
                    </div>
                    <div className="setup-wizard-footer">
                        <hr className="my-2 w-100" />
                        <div className="mb-2 w-75 d-flex justify-content-end">
                            <div>
                                <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                                    Continue <Icon icon="arrow-right" />
                                </Button>
                            </div>
                        </div>
                    </div>
                    <div className="setup-wizard-sidebar">
                        <div className="flex-grow">
                            <NumberedList>
                                {steps.map((step, idx) => (
                                    <SetupWizardStepItem
                                        key={step.title}
                                        isCurrent={step.isCurrent}
                                        isChecked={idx < currentStepIdx}
                                        isInactive={idx > currentStepIdx}
                                        className={classNames("cursor-pointer", { "d-none": !step.isVisible })}
                                        onClick={() => console.log("kalczur go to step", step.title)}
                                    >
                                        <h5 className="mb-0">{step.title}</h5>
                                        <small>{step.description}</small>
                                    </SetupWizardStepItem>
                                ))}
                            </NumberedList>
                        </div>

                        <div>
                            <Icon icon="support" />
                            Having trouble?
                            <p>Our documentation will guide you through the configuration process step by step</p>
                            <Button variant="secondary" className="btn-outline-secondary">
                                See documentation <Icon icon="newtab" />
                            </Button>
                        </div>
                    </div>
                </div>
            </form>
        </FormProvider>
    );
}

interface Step {
    title: SetupWizardStepId;
    description: string;
    component: React.ReactNode;
    isCurrent?: boolean;
    isAvailable?: boolean;
    isVisible?: boolean;
}

function getAvailableSteps(
    currentStep: SetupWizardStepId,
    setupMethod: SetupWizardSetupMethod,
    securityOption: SetupWizardSecurityOption
): Step[] {
    const getIsNotInStepIds = (stepIds: SetupWizardStepId[]) => !stepIds.some((x) => currentStep === x);

    const steps: Step[] = [
        {
            title: "Eula",
            description: "RavenDB Studio Eula",
            component: <SetupWizardEulaStep />,
            isCurrent: currentStep === "Eula",
            isAvailable: true,
            isVisible: false,
        },
        {
            title: "Setup method",
            description: "Chose your setup method",
            component: <SetupWizardSetupMethodStep />,
            isCurrent: currentStep === "Setup method",
            isAvailable: true,
            isVisible: getIsNotInStepIds(["Eula"]),
        },
        {
            title: "Use setup package",
            description: "Use setup package",
            component: <SetupWizardUsePackageStep />,
            isCurrent: currentStep === "Use setup package",
            isAvailable: setupMethod === "usePackage",
            isVisible: getIsNotInStepIds(["Eula", "Setup method"]),
        },
        {
            title: "License key",
            description: "Enter your license key or generate a new one",
            component: <SetupWizardLicenseKeyStep />,
            isCurrent: currentStep === "License key",
            isAvailable: setupMethod === "newCluster",
            isVisible: getIsNotInStepIds(["Eula", "Setup method"]),
        },
        {
            title: "Security",
            description: "Choose security option that fits your needs",
            component: <SetupWizardSecurityStep />,
            isCurrent: currentStep === "Security",
            isAvailable: setupMethod === "newCluster",
            isVisible: getIsNotInStepIds(["Eula", "Setup method"]),
        },
        {
            title: "Self-signed certificate",
            description: "Generate a self-signed certificate",
            component: <SetupWizardSelfSignedCertificateStep />,
            isCurrent: currentStep === "Self-signed certificate",
            isAvailable:
                (setupMethod === "newCluster" || setupMethod === "createPackage") &&
                securityOption === "ownCertificate",
            isVisible: getIsNotInStepIds(["Eula", "Setup method", "Security"]),
        },
        {
            title: "Domain",
            description: "Enter your domain",
            component: <SetupWizardDomainStep />,
            isCurrent: currentStep === "Domain",
            isAvailable:
                (setupMethod === "newCluster" || setupMethod === "createPackage") && securityOption === "letsEncrypt",
            isVisible: getIsNotInStepIds(["Eula", "Setup method", "License key", "Security"]),
        },
        {
            title: "Node address",
            description: "Enter your node address",
            component: <SetupWizardNodeAddressStep />,
            isCurrent: currentStep === "Node address",
            isAvailable: setupMethod === "newCluster" || setupMethod === "createPackage",
            isVisible: getIsNotInStepIds(["Eula", "Setup method", "License key", "Security"]),
        },
        {
            title: "Additional settings",
            description: "Additional settings",
            component: <SetupWizardAdditionalSettingsStep />,
            isCurrent: currentStep === "Additional settings",
            isAvailable: setupMethod === "newCluster" || setupMethod === "createPackage",
            isVisible: getIsNotInStepIds(["Eula", "Setup method", "License key", "Security"]),
        },
        {
            title: "Summary",
            description: "Summary",
            component: <SetupWizardSummaryStep />,
            isCurrent: currentStep === "Summary",
            isAvailable: true,
            isVisible: getIsNotInStepIds(["Eula", "Setup method", "License key", "Security", "Use setup package"]),
        },
        {
            title: "Finish",
            description: "Finish",
            component: <SetupWizardFinishStep />,
            isCurrent: currentStep === "Finish",
            isAvailable: true,
            isVisible: getIsNotInStepIds(["Eula", "Setup method", "License key", "Security", "Use setup package"]),
        },
    ];

    return steps.filter((x) => x.isAvailable);
}
