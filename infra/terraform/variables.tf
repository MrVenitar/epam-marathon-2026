variable "aws_region" {
  description = "AWS region for all resources except CloudFront/ACM globals"
  type        = string
  default     = "us-west-2"
}

variable "account_id" {
  description = "AWS account ID"
  type        = string
  default     = "940108592372"
}

variable "alert_emails" {
  description = "Email addresses subscribed to CloudWatch alerts and Budget notifications. In CI set via the TF_VAR_alert_emails env var (GitHub Actions variable ALERT_EMAILS); locally via a gitignored terraform.tfvars. Value must be HCL/JSON list syntax, e.g. [\"you@example.com\"]."
  type        = list(string)
  default     = []
}

variable "enable_cloudfront" {
  description = "Serve the frontend through CloudFront (HTTPS). New AWS accounts must be verified by AWS Support before CloudFront can be used; until then the frontend is served by S3 static website hosting (HTTP). In CI set via the GitHub variable ENABLE_CLOUDFRONT."
  type        = bool
  default     = false
}
