# Judge check (spec 0004, P6)

Run 2026-11-23 18:25 UTC, judge `claude-haiku-4-5`, answers `claude-sonnet-5-5` (effort Medium), commit `063f759-dirty`.
Score each answer 1–5 yourself in the **Owner** column, without looking at the judge's score first if you can.
Agreement = your score within 1 point of the judge's. Leave a note where you disagree.

| # | Question | Metric | Judge | Owner | Note |
|---|----------|--------|-------|-------|------|
| 1 | rbc-01 | Relevance | 3 | 3 | Agree: gives one KFS element (pre-payment charges) and says the rest is missing; RBC Para 29 / Annex I hold the contents (retrieval miss) |
| 2 | kyc-01 | Relevance | 4 | 4 | Agree: all of Para 45(1)(i)–(v) and 45(2), nothing extra |
| 3 | cf-01 | Groundedness | 5 | 5 | Agree: matches Para 13(1) exactly |
| 4 | dpdp-02 | Relevance | 4 | 4 | Agree: Rule 3(a)–(c) in full, no elaboration |
| 5 | rbc-02 | Groundedness | 5 | 5 | Agree: every claim is in Para 30 (dates, exclusions, penal charges, interest on unpaid EMI) |
| 6 | kyc-02 | Relevance | 4 | 4 | Agree: Para 41(1), six months |
| 7 | cf-02 | Groundedness | 5 | 5 | Agree: matches Para 11(1)–(2) |
| 8 | dpdp-03 | Relevance | 5 | 4 | Within 1: a complete restatement of Rule 6(1)(a)–(g); I see no insight beyond the rule, which is what 5 needs |
| 9 | rbc-03 | Groundedness | 4 | 5 | Within 1: the judge marked it down for not settling the two windows, but the passage text alone cannot settle them. Para 91 sits under H.4 (microfinance) and Para 100 says it excludes microfinance; the prompt sends no headings (see note below) |
| 10 | kyc-03 | Relevance | 4 | 4 | Agree: Para 15(1)–(2) |

## Result

**Agreement: 10/10 within 1 point** (8/10 exact). The two differences go both ways (dpdp-03 judge higher, rbc-03 judge lower).

Who scored: the owner delegated this check to Claude (`claude-opus-5-5`) on 2026-11-24. That makes this a second-model check, not a human one. It is not blind, since the judge's scores were in this sheet. And all three models are Claude, so they may share blind spots. Scores use the judge's own rubric, read from `Microsoft.Extensions.AI.Evaluation.Quality` 10.11.1. Groundedness was checked against the passage text in the database. Relevance was checked against the full paragraph in the corpus.

Found while checking rbc-03 (not fixed here; needs a spec):

- The chunker misses the section heading "I. Responsibilities of Direct Sales Agents (DSA) / Direct Marketing Agents (DMA) / Recovery Agents of the NBFC" (page 27). The section regex accepts only A–H, and the heading spans two lines. As a result, Paras 98–101 carry the heading "H.5 Engagement of recovery agents for recovery of microfinance loans".
- The answer prompt sends no headings. With the right ones, rbc-03 has a clear answer: 8 a.m.–7 p.m. for most loans (Para 100), and 9 a.m.–6 p.m. for microfinance loans (Para 91).

## Answers and the judge's reasons

### rbc-01: What must a Key Fact Statement contain?

The passages do not say what a Key Fact Statement must contain. They only say that an NBFC must provide a KFS as per the Responsible Business Conduct Directions, 2025. They also say that, where a KFS is required, it must mention whether pre-payment charges apply.

Cited: rbi-nbfc-credit-facilities-2025 Para 9, part 1 of 2; rbi-nbfc-responsible-business-conduct-2025 Para 34, part 2 of 2

- **Groundedness 5**: The response deserves a high groundedness score because it accurately and honestly addresses the query by: (1) correctly identifying that the provided context does not contain detailed specifications of KFS contents, (2) accurately citing what the context does say about KFS (that it must be provided per certain Directions), and (3) accurately extracting the one specific requirement mentioned in the context (that pre-payment charges information must be included in the KFS where applicable). The assistant does not introduce extraneous information or make unsupported claims about KFS contents. The response is complete given the limitations of the provided context.
- **Relevance 3**: The response attempts to address the query about KFS requirements but honestly acknowledges that the provided passages do not comprehensively answer the question. It provides one specific mandatory element (mentioning pre-payment charges) that the passages do mention, but this represents only partial information. The response is accurate in what it states but incomplete in addressing the full scope of what a KFS must contain, since the source material itself lacks this comprehensive information. This falls into the Incomplete Response category.

### kyc-01: What extra steps must an NBFC take before opening an account for a politically exposed person?

Besides normal customer due diligence, an NBFC that opens an account for a PEP must have risk management systems to determine whether the customer or beneficial owner is a PEP, take reasonable measures to establish the source of funds or wealth, and get senior management approval to open the account. It must also subject the account to enhanced ongoing monitoring. If an existing customer later becomes a PEP, it needs senior management approval to continue the relationship. These steps also apply to family members and close associates of PEPs.

Cited: rbi-nbfc-kyc-2025 Para 45; rbi-nbfc-kyc-2025 Para 45; rbi-nbfc-kyc-2025 Para 45; rbi-nbfc-kyc-2025 Para 45; rbi-nbfc-kyc-2025 Para 45; rbi-nbfc-kyc-2025 Para 45

- **Groundedness 5**: The response thoroughly and accurately addresses the query by directly citing all five extra steps an NBFC must take when opening accounts for PEPs as outlined in Para 45(1) of the RBI Directions. It includes risk management systems, source of funds verification, senior management approval, enhanced ongoing monitoring, and the requirement for approval if an existing customer becomes a PEP. The response also correctly notes that these requirements extend to family members and close associates. All information is accurate, well-sourced from the provided context, and contains no extraneous or contradictory information.
- **Relevance 4**: The response directly addresses the query by outlining all major extra steps NBFCs must take for PEPs: risk management systems, source of funds verification, senior management approval, enhanced monitoring, and application to related parties. The information is accurate and complete without extraneous details. However, it doesn't provide additional insights, context, or implications that would elevate it to a comprehensive level—it delivers exactly what was asked for without elaboration.

### cf-01: Can a loan app go through the people saved in my phone?

Generally no. An NBFC must ensure that its loan app, and the apps of its lending service providers, do not access your contact list. Any data collection must be need-based and have your prior, explicit consent with an audit trail. One-time access is allowed only for camera, microphone, location or similar facilities needed for onboarding or KYC, and only with your explicit consent.

Cited: rbi-nbfc-credit-facilities-2025 Para 13; rbi-nbfc-credit-facilities-2025 Para 13; rbi-nbfc-credit-facilities-2025 Para 13

- **Groundedness 5**: The response is fully grounded in the context. It directly addresses the user's question about contact list access with an accurate "no," supported by Para 13(1) which explicitly states NBFCs must ensure their apps "desist from accessing mobile phone resources like...contact list." The response accurately includes the permitted one-time access exceptions (camera, microphone, location) for onboarding/KYC with explicit consent, all of which are explicitly stated in the context. No incorrect or unsupported information is introduced.
- **Relevance 5**: The response earns a comprehensive score because it not only directly and accurately answers the user's question ("Generally no"), but also provides valuable regulatory context explaining why this is the case. It elaborates on NBFC obligations, requirements for consent and audit trails, and clarifies what limited access IS permissible. This additional regulatory insight enhances the user's understanding of the underlying rules governing loan app permissions, going beyond a simple yes/no answer while remaining entirely relevant and proportionate.

### dpdp-02: What must a notice from a Data Fiduciary to a Data Principal contain?

The notice must be presented and understandable independently of other information from the Data Fiduciary. It must give, in clear and plain language, a fair account of the details needed for specific and informed consent, including at least an itemised description of the personal data and the specified purposes, with a specific description of the goods, services or uses enabled by the processing. It must also give the communication link to the Data Fiduciary's website or app, and describe other means, if any, to withdraw consent (as easily as it was given), exercise rights under the Act, and complain to the Board.

Cited: dpdp-rules-2025 Rule 3; dpdp-rules-2025 Rule 3; dpdp-rules-2025 Rule 3; dpdp-rules-2025 Rule 3; dpdp-rules-2025 Rule 3; dpdp-rules-2025 Rule 3; dpdp-rules-2025 Rule 3

- **Groundedness 5**: The response thoroughly and accurately addresses the query by directly citing and explaining all requirements from Rule 3 of the Digital Personal Data Protection Rules, 2025. It includes all mandatory elements: independent presentability and understandability, clear language with fair account of necessary details, itemised description of personal data, specified purposes and goods/services, communication links, and means to withdraw consent, exercise rights, and lodge complaints. The information is precise, complete, and entirely supported by the provided context with no inaccuracies or irrelevant additions.
- **Relevance 4**: The response comprehensively addresses the query by providing a detailed list of all mandatory elements that must be included in a notice from a Data Fiduciary to a Data Principal. It covers transparency requirements, clarity standards, specific content mandates (personal data description, purposes, services enabled), and procedural elements (withdrawal, rights exercise, complaints). The information appears accurate based on data protection principles and the response is well-organized and complete without extraneous information.

### rbc-02: Can an NBFC add penal interest to my loan's interest rate when I miss a payment?

No. For loans covered by these rules, an NBFC cannot add penal interest to the interest rate when a borrower defaults or misses a payment. Any penalty must be charged as 'penal charges', which must be reasonable, applied only on the amount in default, and not capitalised. The NBFC may still charge interest on unpaid interest, including unpaid EMI, at the contracted rate until the default is remedied, but not at a penal rate. These rules apply to fresh loans from April 1, 2024, and to existing loans from June 30, 2024, and they exclude credit cards, external commercial borrowings, trade credits, structured obligations and other foreign currency loans.

Cited: rbi-nbfc-responsible-business-conduct-2025 Para 30, part 1 of 3; rbi-nbfc-responsible-business-conduct-2025 Para 30, part 2 of 3; rbi-nbfc-responsible-business-conduct-2025 Para 30, part 1 of 3; rbi-nbfc-responsible-business-conduct-2025 Para 30, part 1 of 3; rbi-nbfc-responsible-business-conduct-2025 Para 30, part 1 of 3

- **Groundedness 5**: The response is fully grounded in the provided context. It directly answers the query by clearly stating that penal interest cannot be added, then provides accurate supporting details including the requirement to use penal charges instead, the conditions under which these charges apply (reasonableness, non-discrimination, non-capitalization), the treatment of interest on unpaid EMI, and the proper scope of applicability (dates and product exclusions). All information is explicitly supported by Para 30 and other relevant sections of the context, with no inaccuracies or unsupported claims.
- **Relevance 5**: The response fully and accurately addresses the query with a clear "No" followed by comprehensive explanation of NBFC penalty rules. It goes beyond basic completeness by providing regulatory context, distinguishing between penal interest (prohibited) and penal charges (allowed with conditions), explaining the scope of applicability, and clarifying what penalties are permitted. This additional regulatory insight and framework information enhances the borrower's understanding of their rights and protections.

### kyc-02: How often must an NBFC review the risk categorisation of its customers' accounts?

An NBFC must have a system of periodic review of the risk categorisation of accounts, with the review done at least once in every six months.

Cited: rbi-nbfc-kyc-2025 Para 41

- **Groundedness 5**: The response directly and accurately answers the query by citing the specific requirement from Para 41(1) of the context. It states that NBFCs must review risk categorisation at least once every six months, which is the exact information provided in the source material. The answer is precise, relevant, complete, and contains no inaccuracies or extraneous information.
- **Relevance 4**: The response directly and accurately answers the query by providing the specific frequency requirement (at least once every six months) for NBFC risk categorisation review. It includes all essential information needed to fully understand the regulatory requirement without extraneous details. However, it does not provide additional context, implications, or elaboration that would elevate it to a comprehensive response with insights.

### cf-02: What is the cooling-off period for digital loans?

There is no single fixed cooling-off period. Each NBFC sets it in its credit policy, but it cannot be shorter than one day. During this period the borrower must be given an explicit option to exit a digital loan by paying the principal and the proportionate APR, without any penalty. The NBFC may keep a reasonable one-time processing fee if the borrower exits during this period, and it must disclose that fee upfront in the KFS.

Cited: rbi-nbfc-credit-facilities-2025 Para 11; rbi-nbfc-credit-facilities-2025 Para 11; rbi-nbfc-credit-facilities-2025 Para 11

- **Groundedness 5**: The response is fully correct and complete. It accurately addresses all aspects of the cooling-off period requirement mentioned in Para 11 of the RBI Directions 2025, including: the variable nature of the period (determined by NBFC), the minimum one-day requirement, the borrower's exit option, the prohibition on penalties, and the optional processing fee disclosure requirement. All details are precisely sourced from the provided context, with no inaccuracies, omissions, or extraneous information.
- **Relevance 5**: The response comprehensively addresses the query about cooling-off periods for digital loans. It goes beyond a simple answer by explaining that the period is variable (set by each NBFC) with a minimum floor of one day, and importantly details borrower protections during this period (exit options, fee structures, disclosure requirements). This provides both complete information and regulatory context that enhances understanding of the concept.

### dpdp-03: What security safeguards must a Data Fiduciary put in place to protect personal data?

A Data Fiduciary must take reasonable security safeguards to prevent personal data breach, covering processing by its Data Processors too. At a minimum these include: data security measures such as encryption, obfuscation, masking or virtual tokens; controls on access to computer resources; logs, monitoring and review to detect unauthorised access, investigate and remediate it; measures for continued processing if data is compromised, such as backups; retaining such logs and personal data for one year unless another law requires otherwise; contract provisions with Data Processors requiring reasonable security safeguards; and appropriate technical and organisational measures to ensure the safeguards are effectively observed.

Cited: dpdp-rules-2025 Rule 6; dpdp-rules-2025 Rule 6; dpdp-rules-2025 Rule 6; dpdp-rules-2025 Rule 6; dpdp-rules-2025 Rule 6; dpdp-rules-2025 Rule 6; dpdp-rules-2025 Rule 6; dpdp-rules-2025 Rule 6

- **Groundedness 5**: The response is comprehensive and accurately reflects all seven security safeguard requirements specified in Rule 6(1) of the context. It directly answers the query without introducing unrelated information, and it includes all relevant details necessary for a complete understanding of the minimum security safeguards required. The information is presented clearly and is entirely supported by the provided context.
- **Relevance 5**: The response fully and accurately addresses the query by providing a comprehensive list of security safeguards required for Data Fiduciaries. It includes all essential categories (encryption, access controls, monitoring, backups, contractual provisions, technical/organizational measures) with specific examples and clarifications. The response also adds relevant contextual insights, such as clarifying that safeguards must extend to Data Processors and specifying retention periods, which enhances understanding of the practical implementation requirements.

### rbc-03: Can a recovery agent call me late at night about an overdue loan?

Generally no. Under one provision, NBFCs and their agents must not call borrowers before 8:00 a.m. or after 7:00 p.m. for recovery of overdue loans, and persistent calling is also barred. That provision does not apply to microfinance loans. Another provision treats calling before 9:00 a.m. or after 6:00 p.m. as a harsh method. The two provisions give different time windows, and the passages do not say how they relate or which one applies to your loan.

Cited: rbi-nbfc-responsible-business-conduct-2025 Para 100; rbi-nbfc-responsible-business-conduct-2025 Para 100; rbi-nbfc-responsible-business-conduct-2025 Para 91

- **Groundedness 4**: The response correctly identifies and accurately cites both relevant provisions from the context (Para 100 and Para 91) with their specific time windows and notes the microfinance exception. However, the response is incomplete because it acknowledges the ambiguity between provisions without providing a sufficiently grounded answer: calling "late at night" is clearly prohibited under Para 100 (after 7:00 p.m.), making the answer "no" definitive on that basis alone. The response captures correct information but lacks the specificity needed to fully address the query based on what the context clearly establishes.
- **Relevance 4**: The response directly and accurately addresses the query about calling times for recovery agents. It provides specific time windows from two different provisions, identifies which entities they apply to (NBFCs vs. microfinance), and mentions related restrictions like persistent calling. While the response doesn't provide a simple definitive answer due to conflicting provisions, it comprehensively explains the regulatory landscape and appropriately acknowledges this ambiguity. This represents a complete and nuanced response rather than an incomplete one.

### kyc-03: What is the Principal Officer responsible for under the KYC Directions?

The Principal Officer is responsible for ensuring compliance, monitoring transactions, and sharing and reporting information as required under the law and regulations. The NBFC must also tell FIU-IND and the RBI the Principal Officer's name, designation, address and contact details.

Cited: rbi-nbfc-kyc-2025 Para 15; rbi-nbfc-kyc-2025 Para 15

- **Groundedness 5**: The response is fully correct and complete. It directly addresses the query by extracting and presenting all the responsibilities of the Principal Officer as defined in Para 15 of the RBI KYC Directions. The assistant accurately captured both subsections: the core responsibilities (compliance, monitoring, reporting) and the administrative requirement for communicating the Principal Officer's details to regulatory authorities. No information is missing, inaccurate, or extraneous.
- **Relevance 4**: The response directly addresses the query about Principal Officer responsibilities under KYC Directions by identifying three core responsibilities: ensuring compliance, monitoring transactions, and sharing/reporting information. The information provided is accurate and covers the essential duties. However, the response lacks comprehensive elaboration on all specific responsibilities that might be outlined in detailed KYC Directions and doesn't provide context or significance beyond the basic statement of duties. The added information about reporting officer details to FIU-IND and RBI, while relevant, is supplementary rather than enhancing the core answer.

