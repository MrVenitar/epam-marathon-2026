resource "aws_cloudfront_origin_access_control" "frontend" {
  count = var.enable_cloudfront ? 1 : 0

  name                              = "restaurant-frontend-oac"
  description                       = "OAC for restaurant-frontend S3 bucket"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

resource "aws_cloudfront_distribution" "frontend" {
  count = var.enable_cloudfront ? 1 : 0

  enabled             = true
  default_root_object = "index.html"
  price_class         = "PriceClass_100" # cheapest tier — US/Canada/Europe edge locations only

  origin {
    domain_name              = aws_s3_bucket.frontend.bucket_regional_domain_name
    origin_id                = "s3-frontend"
    origin_access_control_id = aws_cloudfront_origin_access_control.frontend[0].id
  }

  default_cache_behavior {
    allowed_methods        = ["GET", "HEAD"]
    cached_methods         = ["GET", "HEAD"]
    target_origin_id       = "s3-frontend"
    viewer_protocol_policy = "redirect-to-https"
    compress               = true

    forwarded_values {
      query_string = false
      cookies {
        forward = "none"
      }
    }
  }

  # Angular SPA client-side routing: any path S3 can't resolve gets index.html.
  custom_error_response {
    error_code         = 403
    response_code      = 200
    response_page_path = "/index.html"
  }

  custom_error_response {
    error_code         = 404
    response_code      = 200
    response_page_path = "/index.html"
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = true
  }
}

output "cloudfront_distribution_domain_name" {
  value = var.enable_cloudfront ? aws_cloudfront_distribution.frontend[0].domain_name : null
}

output "cloudfront_distribution_id" {
  value = var.enable_cloudfront ? aws_cloudfront_distribution.frontend[0].id : null
}

output "frontend_url" {
  value = var.enable_cloudfront ? "https://${aws_cloudfront_distribution.frontend[0].domain_name}" : "http://${aws_s3_bucket_website_configuration.frontend[0].website_endpoint}"
}
