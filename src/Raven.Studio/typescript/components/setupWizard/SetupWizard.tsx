import "./SetupWizard.scss";
import Button from "react-bootstrap/Button";
import { Icon } from "components/common/Icon";
import { FormProvider, useForm, useWatch } from "react-hook-form";
import { SetupWizardFormData, setupWizardSchema } from "./setupWizardValidation";
import { yupResolver } from "@hookform/resolvers/yup";
import { NumberedList } from "components/common/NumberedList";
import classNames from "classnames";
import { SetupWizardStepItem } from "./partials/SetupWizardStepItem";
import { useSetupWizardSteps } from "./hooks/useSetupWizardSteps";

const ravenLogo = require("Content/img/ravendb_logo.svg");

export default function SetupWizard() {
    const form = useForm<SetupWizardFormData>({
        resolver: yupResolver(setupWizardSchema),
        defaultValues,
    });

    const { handleSubmit } = form;

    const formValues = useWatch({ control: form.control });

    const steps = useSetupWizardSteps({
        currentStep: formValues.currentStep,
        setupMethod: formValues.setupMethodStep.method,
        securityOption: formValues.securityStep.securityOption,
    });

    const currentStepIdx = steps.findIndex((x) => x.isCurrent);

    return (
        <FormProvider {...form}>
            <form onSubmit={handleSubmit(console.log)} className="h-100">
                <div className="setup-wizard-container">
                    <div className="setup-wizard-main">
                        <div className="d-flex flex-column h-100 w-75">
                            <div className="mt-4 mb-2">
                                <img src={ravenLogo} alt="RavenDB Logo" width="120" />
                            </div>
                            <div>{steps[currentStepIdx].component}</div>
                        </div>
                    </div>
                    <div className="setup-wizard-footer">
                        <hr className="my-2 w-100" />
                        <div className="mb-2 w-75">{steps[currentStepIdx].footer}</div>
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
                            <Button variant="outline-secondary">
                                See documentation <Icon icon="newtab" />
                            </Button>
                        </div>
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
        key: "",
        licenseInfo: null,
        licenseTypeToGenerate: null,
        firstName: "",
        lastName: "",
        email: "",
        phone: "",
    },
    domainStep: {
        domain: "setupwizard.development.run",
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
        nodes: [
            {
                nodeTag: "A",
                nodeUrl: `https://a.maksyms.development.run`,
                httpPort: 8080,
                tcpPort: 38888,
                ipAddress: [
                    {
                        ipAddress: "127.0.0.1",
                    },
                    {
                        ipAddress: "127.0.0.2",
                    },
                    {
                        ipAddress: "127.0.0.3",
                    },
                ],
                hasExternalConfig: false,
                externalIpAddress: undefined,
                externalHttpPort: null,
                externalTcpPort: null,
            },
            {
                nodeTag: "B",
                nodeUrl: `https://b.maksyms.development.run`,
                httpPort: 8082,
                tcpPort: 38889,
                ipAddress: [
                    {
                        ipAddress: "127.0.0.7",
                    },
                    {
                        ipAddress: "127.0.0.8",
                    },
                ],
                hasExternalConfig: true,
                externalIpAddress: "127.0.1.31",
                externalHttpPort: 111,
                externalTcpPort: 222,
            },
            {
                nodeTag: "B",
                nodeUrl: `https://b.maksyms.development.run`,
                httpPort: 8083,
                tcpPort: 38890,
                ipAddress: [
                    {
                        ipAddress: "127.0.0.4",
                    },
                    {
                        ipAddress: "127.0.0.5",
                    },
                    {
                        ipAddress: "127.0.0.6",
                    },
                ],
                hasExternalConfig: true,
                externalIpAddress: "127.0.1.12",
                externalHttpPort: 899,
                externalTcpPort: 919,
            },
        ],
    },
    additionalSettingsStep: {
        isAdvancedSettingsVisible: false,
        dataDirectory: "",
        setupCertificatePath: "",
        adminCertificateExpirationTime: 60,
        postgresqlIntegration: true,
        serverEnvironment: "Production",
    },
};
