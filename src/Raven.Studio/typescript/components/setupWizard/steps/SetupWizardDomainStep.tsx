import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";
import { FormGroup, FormInput, FormLabel, FormSelectCreatable } from "components/common/Form";
import { SelectOption } from "components/common/select/Select";
import React, { useEffect, useState } from "react";
import { useAsyncCallback } from "react-async-hook";
import { useServices } from "hooks/useServices";
import RichAlert from "components/common/RichAlert";

export function SetupWizardDomainStep() {
    const { control, setValue, setError, clearErrors } = useFormContext<SetupWizardFormData>();

    const { domainStep, licenseKeyStep } = useWatch({ control });
    const { setupWizardService } = useServices();
    const { licenseInfo } = licenseKeyStep;
    const [domainsOptions, setDomainsOptions] = useState<SelectOption[]>(
        (Object.keys(licenseInfo.userDomainsWithIps.domains) ?? []).map((domain) => ({
            value: domain,
            label: domain,
        }))
    );
    // TODO What is this? tooltips

    // TODO
    // Jeśli użytkownik ma więcej niż jedną domenę podpiętą pod licencje to zamiast inputu ma creatable select.
    // W przypadku gdy użytkownik wybierze domenę, która jest już w użyciu powinien pojawić się alert o rekordach DNS.

    useDomainFormSideEffects();

    const hasDnsRecords =
        domainStep.domain && Object.keys(licenseInfo.userDomainsWithIps.domains).includes(domainStep.domain);

    const asyncCheckDomainAvailability = useAsyncCallback(async (domain: string) => {
        const key = JSON.parse(licenseKeyStep.key);

        return setupWizardService.checkDomainAvailability(domain, {
            Keys: key,
            Name: "RavenDB Stub Community",
            Id: "Community",
        });
    });

    const handleDomainAvailability = async (domain: string) => {
        const newOption = createNewOption(domain);

        try {
            const domainAvailability = await asyncCheckDomainAvailability.execute(domain);

            if (domainAvailability.Available) {
                clearErrors("domainStep.domain");
                setDomainsOptions((prev) => [...prev, newOption]);
                setValue("domainStep.domain", domain);
                return domain;
            } else {
                setError("domainStep.domain", {
                    type: "value",
                    message: "Domain is already in use",
                });
                return domain;
            }
        } catch (e) {
            // TODO handle error when api is not available
            console.error("Error", e);
        }
    };

    return (
        <div>
            <h2>Domain</h2>
            <p>
                Enter your own domain to be linked with our default hosting zone, where your database will be hosted and
                protected with a security certificate.
            </p>
            <FormGroup>
                <FormLabel className="hstack justify-content-between">
                    <div>Domain</div>
                    <div className="text-info">
                        <Icon icon="info" /> What is this?
                    </div>
                </FormLabel>
                <FormSelectCreatable
                    isLoading={asyncCheckDomainAvailability.loading}
                    onCreateOption={handleDomainAvailability}
                    control={control}
                    name="domainStep.domain"
                    options={domainsOptions}
                    addon={
                        licenseInfo.userDomainsWithIps.rootDomains[0] ?? "" // TODO check if the length of the root domain can exceed 1, paying attention to the fact that it is a array. then I think yes?
                    }
                />
            </FormGroup>
            {hasDnsRecords && (
                <RichAlert icon="info" variant="info">
                    There are some DNS records already set for the selected domain. You will be able to overwrite these
                    settings in the next step.
                </RichAlert>
            )}
            <FormGroup className="mt-3">
                <FormLabel className="hstack justify-content-between">
                    <div>Email</div>
                    <div className="text-info">
                        <Icon icon="info" /> What is this?
                    </div>
                </FormLabel>
                <FormInput type="text" control={control} name="domainStep.email" disabled />
            </FormGroup>
        </div>
    );
}

const useDomainFormSideEffects = () => {
    const { setValue, control } = useFormContext<SetupWizardFormData>();
    const {
        licenseKeyStep: { licenseInfo },
    } = useWatch({ control });

    useEffect(() => {
        if (licenseInfo.userDomainsWithIps.email.length === 1) {
            setValue("domainStep.email", licenseInfo.userDomainsWithIps.email[0]);
        }

        if (licenseInfo.userDomainsWithIps.rootDomains.length === 1) {
            setValue("domainStep.rootDomain", licenseInfo.userDomainsWithIps.rootDomains[0]);
        }

        if (Object.keys(licenseInfo.userDomainsWithIps.domains).length === 1) {
            setValue("domainStep.domain", Object.keys(licenseInfo.userDomainsWithIps.domains)[0]);
        }
    }, []);
};

const createNewOption = (label: string) => ({
    label,
    value: label,
});

export function SetupWizardDomainStepFooter() {
    const { setValue } = useFormContext<SetupWizardFormData>();

    const handleContinue = () => {
        setValue("currentStep", "Node address");
    };

    return (
        <div className="hstack justify-content-end">
            <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}
