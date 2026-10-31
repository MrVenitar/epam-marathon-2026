# One-time bootstrap, run LOCALLY with your own AWS credentials (aws configure):
#
#   cd infra/bootstrap
#   terraform init
#   terraform apply
#
# It creates what the GitHub Actions pipeline needs before it can run:
#   1. the S3 bucket that stores the main Terraform state (infra/terraform/backend.tf)
#   2. the S3 bucket for Syndicate deployment artifacts (syndicate.yml -> deploy_target_bucket)
#   3. a GitHub OIDC identity provider + an IAM role that ONLY this repository can assume,
#      so no long-lived AWS keys are ever stored in GitHub.
#
# Copy the `github_role_arn` output into the repository secret AWS_ROLE_ARN.
# Its own state stays on your machine (terraform.tfstate here, git-ignored).

terraform {
  required_version = ">= 1.10"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

variable "aws_region" {
  type    = string
  default = "us-west-2"
}

variable "github_repository" {
  description = "owner/repo allowed to assume the deploy role"
  type        = string
  default     = "MrVenitar/epam-marathon-2026"
}

provider "aws" {
  region = var.aws_region
}

data "aws_caller_identity" "current" {}

locals {
  account_id = data.aws_caller_identity.current.account_id
}

# --- Terraform state bucket ---

resource "aws_s3_bucket" "tf_state" {
  bucket = "restaurant-terraform-state-${local.account_id}"
}

resource "aws_s3_bucket_versioning" "tf_state" {
  bucket = aws_s3_bucket.tf_state.id
  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "tf_state" {
  bucket = aws_s3_bucket.tf_state.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_public_access_block" "tf_state" {
  bucket                  = aws_s3_bucket.tf_state.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# --- Syndicate artifacts bucket ---

resource "aws_s3_bucket" "syndicate_artifacts" {
  bucket        = "marathon2026-restaurant-artifacts-${local.account_id}"
  force_destroy = true
}

resource "aws_s3_bucket_public_access_block" "syndicate_artifacts" {
  bucket                  = aws_s3_bucket.syndicate_artifacts.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# --- GitHub Actions OIDC ---

resource "aws_iam_openid_connect_provider" "github" {
  url            = "https://token.actions.githubusercontent.com"
  client_id_list = ["sts.amazonaws.com"]
  thumbprint_list = [
    "6938fd4d98bab03faadb97b34396831e3780aea1",
    "1c58a3a8518e8759bf075b76b750d4f2df264fcd",
  ]
}

resource "aws_iam_role" "github_deploy" {
  name = "github-actions-deploy"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect    = "Allow"
        Action    = "sts:AssumeRoleWithWebIdentity"
        Principal = { Federated = aws_iam_openid_connect_provider.github.arn }
        Condition = {
          StringEquals = {
            "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com"
          }
          StringLike = {
            # GitHub sends the repo name with its original casing; allow lower case too.
            "token.actions.githubusercontent.com:sub" = [
              "repo:${var.github_repository}:*",
              "repo:${lower(var.github_repository)}:*",
            ]
          }
        }
      },
    ]
  })
}

# Learning sandbox account: the pipeline creates IAM roles, Cognito, API Gateway, Lambda,
# DynamoDB, S3, CloudFront, CloudWatch and Budgets, so it gets admin rights.
resource "aws_iam_role_policy_attachment" "github_deploy_admin" {
  role       = aws_iam_role.github_deploy.name
  policy_arn = "arn:aws:iam::aws:policy/AdministratorAccess"
}

output "github_role_arn" {
  description = "Put this value into the GitHub repository secret AWS_ROLE_ARN"
  value       = aws_iam_role.github_deploy.arn
}

output "account_id" {
  value = local.account_id
}
