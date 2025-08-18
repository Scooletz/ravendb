import "./SetupWizard.scss";
import Button from "react-bootstrap/Button";
import { Icon } from "components/common/Icon";
import { FormProvider, useForm, useWatch } from "react-hook-form";
import { SetupWizardFormData, setupWizardSchema, SetupWizardStepId } from "./setupWizardValidation";
import { yupResolver } from "@hookform/resolvers/yup";
import { NumberedList } from "components/common/NumberedList";
import classNames from "classnames";
import { SetupWizardStepItem } from "./partials/SetupWizardStepItem";
import { useSetupWizardSteps } from "./hooks/useSetupWizardSteps";
import useConfirm from "components/common/ConfirmDialog";

const ravenLogo = require("Content/img/ravendb_logo.svg");
const ravenSidebarImg = require("Content/img/setupWizard/setup-wizard-sidebar-background.png");

export default function SetupWizard() {
    const form = useForm<SetupWizardFormData>({
        resolver: yupResolver(setupWizardSchema),
        defaultValues,
    });

    const { handleSubmit, control, setValue, getValues } = form;

    const formValues = useWatch({ control });

    const steps = useSetupWizardSteps({
        currentStep: formValues.currentStep,
        setupMethod: formValues.setupMethodStep.method,
        securityOption: formValues.securityStep.securityOption,
    });

    const confirm = useConfirm();

    const currentStepIdx = steps.findIndex((x) => x.isCurrent);

    const getStepKey = (stepTitle: SetupWizardStepId): keyof SetupWizardFormData => {
        const stepKeyMap: Record<SetupWizardStepId, keyof SetupWizardFormData> = {
            Eula: "currentStep", // Not actually used but included for completeness and avoiding TypeScript errors
            "Setup method": "setupMethodStep",
            "Use setup package": "usePackageStep",
            "License key": "licenseKeyStep",
            Domain: "domainStep",
            Security: "securityStep",
            "Self-signed certificate": "selfSignedCertificateStep",
            "Node address": "nodeAddressStep",
            "Additional settings": "additionalSettingsStep",
            Summary: "currentStep", // Not actually used but included for completeness and avoiding TypeScript errors
            Finish: "currentStep", // Not actually used but included for completeness and avoiding TypeScript errors
        };

        return stepKeyMap[stepTitle];
    };

    const handleStepNavigation = async (stepTitle: SetupWizardStepId) => {
        const targetStepIdx = steps.findIndex((s) => s.title === stepTitle);

        if (targetStepIdx > currentStepIdx) {
            return;
        }

        if (targetStepIdx < currentStepIdx) {
            const currentValues = getValues();

            const stepsToClear = steps
                .filter((_, idx) => idx > targetStepIdx && idx <= currentStepIdx)
                .map((s) => s.title);

            if (stepsToClear.length > 0) {
                const isConfirmed = await confirm({
                    title: `Go back to step (${stepTitle})`,
                    message: (
                        <div>
                            <p>Going back to a previous step will clear data for the following steps:</p>
                            <ul>
                                {stepsToClear.map((step) => (
                                    <li key={step}>
                                        <strong>{step}</strong>
                                    </li>
                                ))}
                            </ul>
                            <p>Are you sure you want to continue?</p>
                        </div>
                    ),
                    actionColor: "warning",
                    icon: "warning",
                    confirmText: "Confirm",
                });

                if (!isConfirmed) {
                    return;
                }
            }

            stepsToClear.forEach((stepTitle) => {
                const stepKey = getStepKey(stepTitle);
                if (stepKey !== "currentStep" && stepKey in defaultValues && stepKey in currentValues) {
                    setValue(stepKey, defaultValues[stepKey]);
                }
            });
        }

        setValue("currentStep", stepTitle);
    };

    return (
        <FormProvider {...form}>
            <form onSubmit={handleSubmit(console.log)} className="h-100">
                <div className="setup-wizard-container">
                    <div className="setup-wizard-main">
                        <div className="d-flex flex-column h-100 w-75">
                            <div className="logo-container position-sticky top-0 py-3 z-3">
                                <img src={ravenLogo} alt="RavenDB Logo" width="120" />
                            </div>
                            {steps[currentStepIdx].component}
                        </div>
                    </div>
                    <div className="setup-wizard-footer">
                        <div className="w-75">{steps[currentStepIdx].footer}</div>
                    </div>
                    <div className={classNames("setup-wizard-sidebar", { "p-0": formValues.currentStep === "Eula" })}>
                        {formValues.currentStep === "Eula" ? (
                            <img
                                className="h-100 object-fit-cover"
                                src={ravenSidebarImg}
                                alt={formValues.currentStep}
                            />
                        ) : (
                            <>
                                <div className="flex-grow">
                                    <NumberedList>
                                        {steps.map((step, idx) => (
                                            <SetupWizardStepItem
                                                key={step.title}
                                                isCurrent={step.isCurrent}
                                                isChecked={idx < currentStepIdx}
                                                isInactive={idx > currentStepIdx}
                                                className={classNames("cursor-pointer", {
                                                    "d-none": !step.isVisible,
                                                    "cursor-not-allowed": idx > currentStepIdx,
                                                })}
                                                onClick={() => handleStepNavigation(step.title)}
                                            >
                                                <div className="vstack gap-1">
                                                    <h5 className="mb-0">{step.title}</h5>
                                                    <small className="text-muted">{step.description}</small>
                                                </div>
                                            </SetupWizardStepItem>
                                        ))}
                                    </NumberedList>
                                </div>

                                <div className="d-flex flex-column gap-3">
                                    <Icon icon="lifebuoy" className="fs-3" margin="m-0" />
                                    <div className="vstack gap-1">
                                        <h4 className="mb-0">Having trouble?</h4>
                                        <p className="text-muted mb-0">
                                            Our documentation will guide you through the configuration process step by
                                            step
                                        </p>
                                    </div>
                                    <Button variant="outline-secondary w-fit-content">
                                        See documentation <Icon icon="newtab" margin="m-0" />
                                    </Button>
                                </div>
                            </>
                        )}
                    </div>
                </div>
            </form>
        </FormProvider>
    );
}

const defaultValues: SetupWizardFormData = {
    currentStep: "Eula",
    setupMethodStep: {
        method: null,
    },
    usePackageStep: {
        fileZip: "",
        nodeTag: "",
    },
    licenseKeyStep: {
        isAcceptTerms: false,
        isAcceptEmails: false,
        key: "",
        licenseInfo: null,
        licenseTypeToGenerate: null,
        firstName: "",
        lastName: "",
        email: "",
        phone: "",
    },
    domainStep: {
        domain: "",
        email: "todo@todo.com",
    },
    securityStep: {
        securityOption: null,
    },
    selfSignedCertificateStep: {
        certificateFileName: "",
        certificate: "",
        password: "",
        cns: [],
    },
    nodeAddressStep: {
        nodes: [],
    },
    additionalSettingsStep: {
        isAdvancedSettingsVisible: false,
        dataDirectory: "",
        setupCertificatePath: "",
        adminCertificateExpirationTime: 120,
        postgresqlIntegration: false,
        serverEnvironment: "Production",
    },
};
