import Button from "react-bootstrap/Button";
import { Icon } from "components/common/Icon";
import { FormProvider, useForm, useWatch } from "react-hook-form";
import {
    SetupWizardFormData,
    setupWizardSchema,
    SetupWizardSetupMethod,
    SetupWizardStepId,
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
                method: "newCluster",
            },
        },
    });

    const { setValue, handleSubmit } = form;

    const formValues = useWatch({ control: form.control });

    const steps = useMemo(
        () => getAvailableSteps(formValues.currentStep, formValues.setupMethodStep.method),
        [formValues.currentStep, formValues.setupMethodStep.method]
    );

    const currentStepIdx = steps.findIndex((x) => x.isCurrent);

    const handleContinue = () => {
        setValue("currentStep", steps[currentStepIdx + 1].title);
    };

    return (
        <FormProvider {...form}>
            <form onSubmit={handleSubmit(console.log)} className="h-100">
                <div className="hstack h-100">
                    <div className="vstack flex-grow-1 h-100">
                        <div>
                            <img src={ravenLogo} alt="RavenDB Logo" width="120" />
                        </div>
                        <div className="h-100">{steps[currentStepIdx].component}</div>
                        <hr />
                        <div className="d-flex justify-content-end">
                            <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                                Continue <Icon icon="arrow-right" />
                            </Button>
                        </div>
                    </div>
                    <div className="h-100 d-flex flex-column justify-content-between p-4" style={{ width: 300 }}>
                        <div>
                            <NumberedList>
                                {steps.map((step, idx) => (
                                    <SetupWizardStepItem
                                        key={step.title}
                                        isCurrent={step.isCurrent}
                                        isChecked={idx < currentStepIdx}
                                        isInactive={idx > currentStepIdx}
                                        className={classNames({ "d-none": idx > currentStepIdx || step.isHidden })}
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
    isHidden?: boolean;
}

function getAvailableSteps(currentStep: SetupWizardStepId, setupMethod: SetupWizardSetupMethod): Step[] {
    // const getisHidden = (stepIds: SetupWizardStepId[]) => {
    //     const currentStepIdx = steps.findIndex((x) => x.isCurrent);

    //     return stepIds.some((x) => currentStep === x);
    // };

    // TODO wszystkie kroki wstecz powinny być widoczne
    // dodatkowo widoczne powinny być niektre kroki w zaleznosci od wyboru np security

    const steps: Step[] = [
        {
            title: "Eula",
            description: "RavenDB Studio Eula",
            component: <SetupWizardEulaStep />,
            isCurrent: currentStep === "Eula",
            isAvailable: true,
            isHidden: false,
        },
        {
            title: "Setup method",
            description: "Chose your setup method",
            component: <SetupWizardSetupMethodStep />,
            isCurrent: currentStep === "Setup method",
            isAvailable: true,
            isHidden: false,
        },
        {
            title: "Use setup package",
            description: "Use setup package",
            component: <SetupWizardUsePackageStep />,
            isCurrent: currentStep === "Use setup package",
            isAvailable: setupMethod === "usePackage",
            isHidden: false,
        },
        {
            title: "License key",
            description: "Enter your license key or generate a new one",
            component: <SetupWizardLicenseKeyStep />,
            isCurrent: currentStep === "License key",
            isAvailable: setupMethod === "newCluster" || setupMethod === "createPackage",
            isHidden: false,
        },
        {
            title: "Security",
            description: "Choose security option that fits your needs",
            component: <SetupWizardSecurityStep />,
            isCurrent: currentStep === "Security",
            isAvailable: setupMethod === "newCluster" || setupMethod === "createPackage",
            isHidden: currentStep === "Security",
        },
        {
            title: "Self-signed certificate",
            description: "Generate a self-signed certificate",
            component: <SetupWizardSelfSignedCertificateStep />,
            isCurrent: currentStep === "Self-signed certificate",
            isAvailable: setupMethod === "newCluster" || setupMethod === "createPackage",
            isHidden: true,
        },
        {
            title: "Domain",
            description: "Enter your domain",
            component: <SetupWizardDomainStep />,
            isCurrent: currentStep === "Domain",
            isAvailable: setupMethod === "newCluster" || setupMethod === "createPackage",
            isHidden: true,
        },
        {
            title: "Node address",
            description: "Enter your node address",
            component: <SetupWizardNodeAddressStep />,
            isCurrent: currentStep === "Node address",
            isAvailable: setupMethod === "newCluster" || setupMethod === "createPackage",
            isHidden: true,
        },
        {
            title: "Additional settings",
            description: "Additional settings",
            component: <SetupWizardAdditionalSettingsStep />,
            isCurrent: currentStep === "Additional settings",
            isAvailable: setupMethod === "newCluster" || setupMethod === "createPackage",
            isHidden: true,
        },
        {
            title: "Summary",
            description: "Summary",
            component: <SetupWizardSummaryStep />,
            isCurrent: currentStep === "Summary",
            isAvailable: true,
            isHidden: true,
        },
        {
            title: "Finish",
            description: "Finish",
            component: <SetupWizardFinishStep />,
            isCurrent: currentStep === "Finish",
            isAvailable: true,
            isHidden: true,
        },
    ];

    return steps.filter((x) => x.isAvailable);
}
